import { createHash, timingSafeEqual } from "node:crypto";
import http from "node:http";
import { CostLedger } from "./budget.js";
import { GatewayError, errorEnvelope } from "./errors.js";
import { isLoopbackHost } from "./config.js";
import { OpenRouterClient, buildOpenRouterRequest } from "./openrouter.js";
import {
  GenerationTracker,
  IdempotencyStore,
  RoundRobinScheduler,
  SlidingWindowRateLimiter,
} from "./scheduler.js";
import { stableStringify, validatePlanRequest } from "./validation.js";

function safeLogger(output = console) {
  const counts = new Map();
  return {
    event(name, fields = {}) {
      const count = (counts.get(name) || 0) + 1;
      counts.set(name, count);
      const safeFields = {};
      for (const [key, value] of Object.entries(fields)) {
        if (["status", "queued", "durationMs"].includes(key) && Number.isFinite(value)) safeFields[key] = value;
      }
      output.error(JSON.stringify({ service: "riverworks-resident-gateway", event: name, count, ...safeFields }));
    },
  };
}

function secureEqual(left, right) {
  const leftBuffer = Buffer.from(left, "utf8");
  const rightBuffer = Buffer.from(right, "utf8");
  return leftBuffer.length === rightBuffer.length && timingSafeEqual(leftBuffer, rightBuffer);
}

function allowedOrigin(origin) {
  if (!origin) return true;
  try {
    const url = new URL(origin);
    return (url.protocol === "http:" || url.protocol === "https:") && isLoopbackHost(url.hostname);
  } catch {
    return false;
  }
}

function writeJson(response, status, body, origin) {
  if (response.destroyed || response.writableEnded) return;
  const payload = JSON.stringify(body);
  const headers = {
    "Content-Type": "application/json; charset=utf-8",
    "Content-Length": Buffer.byteLength(payload),
    "Cache-Control": "no-store",
    "X-Content-Type-Options": "nosniff",
  };
  if (origin) {
    headers["Access-Control-Allow-Origin"] = origin;
    headers.Vary = "Origin";
  }
  response.writeHead(status, headers);
  response.end(payload);
}

function writeNoContent(response, origin) {
  const headers = {
    "Cache-Control": "no-store",
    "Access-Control-Allow-Origin": origin,
    "Access-Control-Allow-Methods": "POST, GET, OPTIONS",
    "Access-Control-Allow-Headers": "Content-Type, Authorization",
    "Access-Control-Max-Age": "600",
    Vary: "Origin",
  };
  response.writeHead(204, headers);
  response.end();
}

async function readJson(request, maxBodyBytes) {
  const contentType = request.headers["content-type"] || "";
  if (!/^application\/json(?:\s*;|$)/iu.test(contentType)) {
    throw new GatewayError(415, "json_content_type_required", "Content-Type must be application/json.");
  }
  const declaredLength = Number(request.headers["content-length"]);
  if (Number.isFinite(declaredLength) && declaredLength > maxBodyBytes) {
    request.resume();
    throw new GatewayError(413, "request_too_large", "The request body exceeds 64 KiB.");
  }

  const chunks = [];
  let total = 0;
  for await (const chunk of request) {
    total += chunk.length;
    if (total > maxBodyBytes) {
      throw new GatewayError(413, "request_too_large", "The request body exceeds 64 KiB.");
    }
    chunks.push(chunk);
  }
  if (total === 0) throw new GatewayError(400, "invalid_json", "The request body must contain JSON.");
  try {
    return JSON.parse(Buffer.concat(chunks).toString("utf8"));
  } catch {
    throw new GatewayError(400, "invalid_json", "The request body contains malformed JSON.");
  }
}

function bearerToken(request) {
  const authorization = request.headers.authorization;
  if (typeof authorization !== "string") return "";
  const match = /^Bearer\s+(.+)$/iu.exec(authorization);
  return match ? match[1] : "";
}

export function createGateway({
  config,
  fetchImpl = globalThis.fetch,
  now = Date.now,
  logger = safeLogger(),
  costLedger,
} = {}) {
  if (!config) throw new Error("Gateway config is required.");
  const scheduler = new RoundRobinScheduler();
  const rateLimiter = new SlidingWindowRateLimiter(config.ratePerMinute, now);
  const idempotency = new IdempotencyStore(config.idempotencyTtlMs, now);
  const generations = new GenerationTracker(config.idempotencyTtlMs, now);
  const ledger = costLedger || new CostLedger({
    dataDir: config.dataDir,
    maxUsdPerHour: config.maxUsdPerHour,
    maxUsdPerDay: config.maxUsdPerDay,
    now,
    onWriteError: (event) => logger.event(event),
  });
  const provider = new OpenRouterClient(config, fetchImpl);
  const requiresClientAuth = !isLoopbackHost(config.host);

  async function processPlan(plan) {
    if (!config.apiKey) {
      throw new GatewayError(503, "provider_not_configured", "Resident AI is unavailable until the server API key is configured.");
    }
    if (!generations.accept(plan.sessionId, plan.generation, plan.requestId)) {
      throw new GatewayError(409, "stale_generation", "A newer or different request for this generation already exists.");
    }
    if (!rateLimiter.take()) {
      throw new GatewayError(429, "rate_limit_exceeded", `The resident AI request limit is ${config.ratePerMinute} plans per minute.`);
    }

    const prepared = buildOpenRouterRequest(plan, config);
    const reservation = await ledger.reserve(prepared.maxEstimatedUsd);
    let attempted = false;
    try {
      const result = await scheduler.enqueue(plan.sessionId, async () => {
        attempted = true;
        return provider.execute(plan, prepared);
      });
      await ledger.commit(reservation, result.usage.estimatedUsd);
      return {
        status: 200,
        body: {
          protocol: 1,
          requestId: plan.requestId,
          generation: plan.generation,
          source: "openrouter",
          model: config.model,
          generatedAt: new Date(now()).toISOString(),
          decisions: result.decisions,
          usage: result.usage,
        },
      };
    } catch (error) {
      if (attempted) await ledger.commit(reservation, undefined);
      else ledger.release(reservation);
      throw error;
    }
  }

  async function handle(request, response) {
    const origin = typeof request.headers.origin === "string" ? request.headers.origin : "";
    if (!allowedOrigin(origin)) {
      writeJson(response, 403, { error: { code: "origin_denied", message: "This request origin is not allowed." } });
      return;
    }

    if (request.method === "OPTIONS") {
      if (!origin) {
        writeJson(response, 400, { error: { code: "origin_required", message: "CORS preflight requires an Origin header." } });
      } else {
        writeNoContent(response, origin);
      }
      return;
    }

    if (requiresClientAuth && !secureEqual(bearerToken(request), config.gatewayToken)) {
      writeJson(response, 401, { error: { code: "unauthorized", message: "A valid gateway Bearer token is required." } }, origin);
      return;
    }

    const url = new URL(request.url || "/", "http://gateway.local");
    if (request.method === "GET" && url.pathname === "/health") {
      writeJson(response, 200, { ready: Boolean(config.apiKey), provider: config.provider, model: config.model }, origin);
      return;
    }
    if (request.method !== "POST" || url.pathname !== "/v1/citizens/plan") {
      writeJson(response, 404, { error: { code: "not_found", message: "The requested gateway route does not exist." } }, origin);
      return;
    }

    let plan;
    try {
      plan = validatePlanRequest(await readJson(request, config.maxBodyBytes));
    } catch (error) {
      const result = errorEnvelope(error);
      logger.event(result.body.error.code, { status: result.status });
      writeJson(response, result.status, result.body, origin);
      return;
    }

    const payloadHash = createHash("sha256").update(stableStringify(plan)).digest("hex");
    const entry = idempotency.begin(plan.requestId, payloadHash);
    if (entry.kind === "collision") {
      writeJson(response, 409, { error: { code: "request_id_collision", message: "This requestId was already used with a different payload." } }, origin);
      return;
    }

    let result;
    if (entry.kind === "replay") {
      result = await entry.promise;
    } else {
      try {
        result = await processPlan(plan);
      } catch (error) {
        result = errorEnvelope(error);
        logger.event(result.body.error.code, { status: result.status, queued: scheduler.queuedCount });
      }
      entry.complete(result);
    }
    writeJson(response, result.status, result.body, origin);
  }

  const server = http.createServer((request, response) => {
    handle(request, response).catch(() => {
      logger.event("request_handler_failed", { status: 500 });
      writeJson(response, 500, errorEnvelope(new Error()).body);
    });
  });
  server.requestTimeout = config.upstreamTimeoutMs + 10_000;
  server.headersTimeout = 10_000;
  server.keepAliveTimeout = 5_000;
  return server;
}

export { safeLogger };
