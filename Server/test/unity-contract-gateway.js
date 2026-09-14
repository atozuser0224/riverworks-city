import http from "node:http";

const DEFAULT_PORT = 47842;
const HOST = "127.0.0.1";
const MAX_BODY_BYTES = 64 * 1024;
const MOODS = new Set(["calm", "happy", "worried", "curious", "tired"]);
const portArgument = process.argv.indexOf("--port");
const port = portArgument >= 0 ? Number.parseInt(process.argv[portArgument + 1], 10) : DEFAULT_PORT;

if (!Number.isSafeInteger(port) || port < 1024 || port > 65535) {
  throw new Error("--port must be an integer between 1024 and 65535.");
}

const stats = {
  healthRequests: 0,
  planRequests: 0,
  validRequestShapes: true,
  idsAreStrings: true,
  contentTypeJson: true,
  secretFreeRequestUrls: true,
  authorizationHeaders: 0,
  duplicateRequestIds: 0,
  lastRequestId: "",
};
const requestIds = new Set();

function writeJson(response, status, body) {
  const payload = JSON.stringify(body);
  response.writeHead(status, {
    "Content-Type": "application/json; charset=utf-8",
    "Content-Length": Buffer.byteLength(payload),
    "Cache-Control": "no-store",
    "X-Content-Type-Options": "nosniff",
  });
  response.end(payload);
}

async function readJson(request) {
  const chunks = [];
  let size = 0;
  for await (const chunk of request) {
    size += chunk.length;
    if (size > MAX_BODY_BYTES) throw new Error("request too large");
    chunks.push(chunk);
  }
  return JSON.parse(Buffer.concat(chunks).toString("utf8"));
}

function requestShapeIsValid(plan) {
  if (!plan || typeof plan !== "object" || Array.isArray(plan)) return false;
  const exactKeys = ["protocol", "sessionId", "requestId", "generation", "simulationDay", "city", "residents"];
  if (Object.keys(plan).sort().join("|") !== exactKeys.sort().join("|")) return false;
  if (plan.protocol !== 1 || typeof plan.sessionId !== "string" ||
      !/^[a-f0-9]{32}$/iu.test(plan.requestId) || !Number.isSafeInteger(plan.generation) ||
      !Array.isArray(plan.residents) || plan.residents.length < 1 || plan.residents.length > 16) return false;

  const ids = new Set();
  for (const resident of plan.residents) {
    if (!resident || typeof resident.id !== "string" || ids.has(resident.id) ||
        !Array.isArray(resident.allowed) || resident.allowed.length < 1) return false;
    ids.add(resident.id);
    for (const choice of resident.allowed) {
      if (!choice || typeof choice.intent !== "string" || !Number.isSafeInteger(choice.target)) return false;
    }
  }
  return true;
}

function decisionFor(resident, index) {
  const choice = resident.allowed[index % resident.allowed.length];
  return {
    id: resident.id,
    intent: choice.intent,
    target: choice.target,
    dwellSeconds: 12,
    thought: "I will follow the <allowed> road route.",
    memory: "The contract fixture plan stays only in this running game session.",
    mood: MOODS.has("curious") ? "curious" : "calm",
  };
}

function planResponse(plan, mode) {
  let decisions = plan.residents.map(decisionFor);
  if (mode === "disallowed") {
    decisions = decisions.map((decision) => ({ ...decision, intent: "teleport", target: 1000000 }));
  } else if (mode === "duplicate" && decisions.length > 1) {
    decisions[1] = { ...decisions[1], id: decisions[0].id };
  }
  return {
    protocol: 1,
    requestId: plan.requestId,
    generation: mode === "stale" ? Math.max(0, plan.generation - 1) : plan.generation,
    source: "openrouter",
    model: "contract-fixture",
    generatedAt: "2026-09-14T00:00:00.000Z",
    decisions,
    usage: { promptTokens: 0, completionTokens: 0, estimatedUsd: 0 },
  };
}

function modeFor(pathname) {
  const segments = pathname.split("/").filter(Boolean);
  return segments.length > 0 ? segments[0] : "success";
}

const server = http.createServer(async (request, response) => {
  const url = new URL(request.url || "/", "http://contract-fixture.local");
  const mode = modeFor(url.pathname);
  stats.secretFreeRequestUrls = stats.secretFreeRequestUrls && !url.username && !url.password &&
    !url.search && !/api[_-]?key|token|authorization/iu.test(request.url || "");
  if (typeof request.headers.authorization === "string") stats.authorizationHeaders += 1;

  if (request.method === "GET" && url.pathname.endsWith("/stats")) {
    writeJson(response, 200, stats);
    return;
  }
  if (request.method === "GET" && url.pathname.endsWith("/health")) {
    stats.healthRequests += 1;
    writeJson(response, 200, {
      ready: mode !== "not-ready",
      provider: "openrouter",
      model: "contract-fixture",
    });
    return;
  }
  if (request.method !== "POST" || !url.pathname.endsWith("/v1/citizens/plan")) {
    writeJson(response, 404, { error: { code: "not_found", message: "Unknown fixture route." } });
    return;
  }

  try {
    const plan = await readJson(request);
    stats.planRequests += 1;
    stats.contentTypeJson = stats.contentTypeJson &&
      /^application\/json(?:\s*;|$)/iu.test(request.headers["content-type"] || "");
    stats.idsAreStrings = stats.idsAreStrings && plan.residents?.every((resident) => typeof resident.id === "string");
    const validShape = requestShapeIsValid(plan);
    stats.validRequestShapes = stats.validRequestShapes && validShape;
    if (!validShape) {
      writeJson(response, 400, { error: { code: "invalid_request", message: "Unity request shape is invalid." } });
      return;
    }
    if (requestIds.has(plan.requestId)) stats.duplicateRequestIds += 1;
    requestIds.add(plan.requestId);
    stats.lastRequestId = plan.requestId;

    if (mode === "malformed") {
      response.writeHead(200, { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store" });
      response.end("{\"protocol\":");
      return;
    }
    if (mode === "slow") await new Promise((resolve) => setTimeout(resolve, 750));
    writeJson(response, 200, planResponse(plan, mode));
  } catch {
    writeJson(response, 400, { error: { code: "invalid_json", message: "Request body must be valid JSON." } });
  }
});

server.requestTimeout = 10_000;
server.headersTimeout = 5_000;
server.keepAliveTimeout = 2_000;
server.listen(port, HOST, () => {
  process.stdout.write(`RIVERWORKS_UNITY_CONTRACT_GATEWAY_READY http://${HOST}:${port}\n`);
});

const close = () => server.close(() => process.exit(0));
process.once("SIGINT", close);
process.once("SIGTERM", close);
