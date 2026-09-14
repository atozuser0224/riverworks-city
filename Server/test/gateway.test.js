import assert from "node:assert/strict";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import http from "node:http";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { CostLedger } from "../src/budget.js";
import { loadConfig } from "../src/config.js";
import { GatewayError } from "../src/errors.js";
import { createGateway } from "../src/server.js";
import { RoundRobinScheduler } from "../src/scheduler.js";

const TEST_KEY = "test-openrouter-key-never-expose";
const TEST_GATEWAY_TOKEN = "test-gateway-token-never-expose";

function makeConfig(overrides = {}) {
  return {
    serverDir: os.tmpdir(),
    dataDir: path.join(os.tmpdir(), "riverworks-gateway-tests"),
    host: "127.0.0.1",
    port: 47841,
    gatewayToken: "",
    apiKey: TEST_KEY,
    model: "google/gemini-2.5-flash-lite",
    pricing: { inputUsdPerMillion: 0.10, outputUsdPerMillion: 0.40 },
    provider: "openrouter",
    upstreamUrl: "https://openrouter.ai/api/v1/chat/completions",
    ratePerMinute: 2,
    maxUsdPerHour: 0.5,
    maxUsdPerDay: 2,
    upstreamTimeoutMs: 30_000,
    maxBodyBytes: 64 * 1024,
    idempotencyTtlMs: 120_000,
    maxOutputTokens: 2_048,
    temperature: 0.6,
    ...overrides,
  };
}

function makePlan({
  requestId = "0123456789abcdef0123456789abcdef",
  generation = 1,
  sessionId = "session-a",
  residentId = "resident-1",
} = {}) {
  return {
    protocol: 1,
    sessionId,
    requestId,
    generation,
    simulationDay: 12,
    city: { era: "industrial", happiness: 72.5, grain: 48, bread: 19, coins: 310 },
    residents: [
      {
        id: residentId,
        name: "Mina",
        persona: "Curious mill worker who likes quiet parks.",
        activity: "idle",
        home: 44,
        job: 120,
        current: 44,
        allowed: [
          { intent: "work", target: 120 },
          { intent: "park", target: 355 },
        ],
        recentMemory: "I bought bread yesterday.",
      },
    ],
  };
}

function providerResponse(plan, overrides = {}) {
  const decision = {
    id: plan.residents[0].id,
    intent: "park",
    target: 355,
    dwellSeconds: 12,
    thought: "A short walk will clear my head.",
    memory: "I rested beneath the park trees.",
    mood: "curious",
    ...overrides.decision,
  };
  return new Response(JSON.stringify({
    id: "provider-response-id",
    choices: [{
      finish_reason: overrides.finishReason ?? "stop",
      message: { content: overrides.content ?? JSON.stringify({ decisions: [decision] }) },
    }],
    usage: overrides.usage ?? { prompt_tokens: 500, completion_tokens: 100 },
  }), { status: overrides.status ?? 200, headers: { "Content-Type": "application/json" } });
}

class FakeLedger {
  reservations = [];
  commits = [];
  releases = [];
  error;

  async reserve(amount) {
    if (this.error) throw this.error;
    const reservation = { id: `reservation-${this.reservations.length}`, maxEstimatedUsd: amount };
    this.reservations.push(reservation);
    return reservation;
  }

  async commit(reservation, amount) {
    this.commits.push({ reservation, amount });
  }

  release(reservation) {
    this.releases.push(reservation);
  }
}

function testLogger() {
  const events = [];
  return { events, event(name, fields = {}) { events.push({ name, fields }); } };
}

async function listen(server) {
  await new Promise((resolve, reject) => {
    server.once("error", reject);
    server.listen(0, "127.0.0.1", resolve);
  });
  const address = server.address();
  return `http://127.0.0.1:${address.port}`;
}

async function close(server) {
  await new Promise((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
}

async function withGateway(options, run) {
  const ledger = options.costLedger || new FakeLedger();
  const logger = options.logger || testLogger();
  const server = createGateway({ ...options, costLedger: ledger, logger });
  const baseUrl = await listen(server);
  try {
    await run({ baseUrl, ledger, logger });
  } finally {
    await close(server);
  }
}

async function post(baseUrl, plan, headers = {}) {
  return fetch(`${baseUrl}/v1/citizens/plan`, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...headers },
    body: JSON.stringify(plan),
  });
}

test("health reports only readiness, provider, and model without exposing credentials", async () => {
  await withGateway({ config: makeConfig({ apiKey: "" }), fetchImpl: () => assert.fail("fetch must not run") }, async ({ baseUrl }) => {
    const response = await fetch(`${baseUrl}/health`);
    assert.equal(response.status, 200);
    assert.deepEqual(await response.json(), {
      ready: false,
      provider: "openrouter",
      model: "google/gemini-2.5-flash-lite",
    });

    const planResponse = await post(baseUrl, makePlan());
    assert.equal(planResponse.status, 503);
    const text = await planResponse.text();
    assert.doesNotMatch(text, /openrouter-key/u);
    assert.equal(JSON.parse(text).error.code, "provider_not_configured");
  });
});

test("valid plan uses strict OpenRouter structured output and returns a verified envelope", async () => {
  let upstreamCalls = 0;
  let captured;
  const logger = testLogger();
  const plan = makePlan();
  await withGateway({
    config: makeConfig(),
    logger,
    fetchImpl: async (url, options) => {
      upstreamCalls += 1;
      captured = { url, options, body: JSON.parse(options.body) };
      return providerResponse(plan);
    },
    now: () => Date.parse("2026-09-14T00:00:00.000Z"),
  }, async ({ baseUrl, ledger }) => {
    const response = await post(baseUrl, plan);
    assert.equal(response.status, 200);
    const responseText = await response.text();
    const body = JSON.parse(responseText);
    assert.equal(body.protocol, 1);
    assert.equal(body.requestId, plan.requestId);
    assert.equal(body.generation, 1);
    assert.equal(body.source, "openrouter");
    assert.equal(body.model, "google/gemini-2.5-flash-lite");
    assert.equal(body.generatedAt, "2026-09-14T00:00:00.000Z");
    assert.deepEqual(body.decisions[0], JSON.parse(captured.body.messages[1].content).residents[0]
      ? {
          id: "resident-1",
          intent: "park",
          target: 355,
          dwellSeconds: 12,
          thought: "A short walk will clear my head.",
          memory: "I rested beneath the park trees.",
          mood: "curious",
        }
      : assert.fail("resident facts missing"));
    assert.deepEqual(body.usage, { promptTokens: 500, completionTokens: 100, estimatedUsd: 0.00009 });
    assert.equal(ledger.commits.length, 1);
    assert.equal(ledger.commits[0].amount, 0.00009);
    assert.doesNotMatch(responseText, new RegExp(TEST_KEY, "u"));
  });

  assert.equal(upstreamCalls, 1);
  assert.equal(captured.url, "https://openrouter.ai/api/v1/chat/completions");
  assert.equal(captured.options.headers.Authorization, `Bearer ${TEST_KEY}`);
  assert.equal(captured.body.model, "google/gemini-2.5-flash-lite");
  assert.equal(captured.body.max_tokens, 2048);
  assert.equal(captured.body.temperature, 0.6);
  assert.deepEqual(captured.body.provider, { require_parameters: true });
  assert.equal(captured.body.response_format.type, "json_schema");
  assert.equal(captured.body.response_format.json_schema.strict, true);
  assert.equal(captured.body.response_format.json_schema.schema.additionalProperties, false);
  assert.equal(Object.hasOwn(captured.body, "tools"), false);
  assert.doesNotMatch(JSON.stringify(logger.events), new RegExp(TEST_KEY, "u"));
});

test("malformed, truncated, and disallowed provider plans fail closed", async (t) => {
  const cases = [
    {
      name: "malformed nested JSON",
      response: (plan) => providerResponse(plan, { content: "not json" }),
      code: "invalid_provider_response",
    },
    {
      name: "truncated completion",
      response: (plan) => providerResponse(plan, { finishReason: "length" }),
      code: "truncated_provider_response",
    },
    {
      name: "choice outside resident allowlist",
      response: (plan) => providerResponse(plan, { decision: { intent: "home", target: 44 } }),
      code: "invalid_provider_response",
    },
    {
      name: "overlong thought",
      response: (plan) => providerResponse(plan, { decision: { thought: "x".repeat(101) } }),
      code: "invalid_provider_response",
    },
  ];

  for (const [index, current] of cases.entries()) {
    await t.test(current.name, async () => {
      const plan = makePlan({ requestId: index.toString(16).padStart(32, "0") });
      await withGateway({ config: makeConfig(), fetchImpl: async () => current.response(plan) }, async ({ baseUrl, ledger }) => {
        const response = await post(baseUrl, plan);
        assert.equal(response.status, 502);
        const text = await response.text();
        const body = JSON.parse(text);
        assert.equal(body.error.code, current.code);
        assert.equal(Object.hasOwn(body, "decisions"), false);
        assert.equal(Object.hasOwn(body, "source"), false);
        assert.equal(ledger.commits.length, 1, "an attempted request reserves conservative cost");
        assert.equal(ledger.commits[0].amount, undefined);
      });
    });
  }
});

test("idempotent replay performs one paid call and payload collision is rejected", async () => {
  let upstreamCalls = 0;
  const plan = makePlan();
  await withGateway({
    config: makeConfig(),
    fetchImpl: async () => {
      upstreamCalls += 1;
      return providerResponse(plan);
    },
  }, async ({ baseUrl }) => {
    const first = await post(baseUrl, plan);
    const second = await post(baseUrl, structuredClone(plan));
    assert.equal(first.status, 200);
    assert.equal(second.status, 200);
    assert.deepEqual(await second.json(), await first.json());
    assert.equal(upstreamCalls, 1);

    const collision = structuredClone(plan);
    collision.city.coins += 1;
    const collisionResponse = await post(baseUrl, collision);
    assert.equal(collisionResponse.status, 409);
    assert.equal((await collisionResponse.json()).error.code, "request_id_collision");
    assert.equal(upstreamCalls, 1);
  });
});

test("concurrent replay joins the original in-flight request", async () => {
  let releaseProvider;
  let upstreamCalls = 0;
  const plan = makePlan();
  await withGateway({
    config: makeConfig(),
    fetchImpl: async () => {
      upstreamCalls += 1;
      await new Promise((resolve) => { releaseProvider = resolve; });
      return providerResponse(plan);
    },
  }, async ({ baseUrl }) => {
    const first = post(baseUrl, plan);
    while (!releaseProvider) await new Promise((resolve) => setImmediate(resolve));
    const second = post(baseUrl, plan);
    await new Promise((resolve) => setImmediate(resolve));
    releaseProvider();
    const [firstResponse, secondResponse] = await Promise.all([first, second]);
    assert.equal(firstResponse.status, 200);
    assert.equal(secondResponse.status, 200);
    assert.equal(upstreamCalls, 1);
  });
});

test("older or duplicate generations with a different requestId are rejected", async () => {
  let upstreamCalls = 0;
  const newest = makePlan({ generation: 5, requestId: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" });
  await withGateway({
    config: makeConfig(),
    fetchImpl: async () => {
      upstreamCalls += 1;
      return providerResponse(newest);
    },
  }, async ({ baseUrl }) => {
    assert.equal((await post(baseUrl, newest)).status, 200);
    const stale = makePlan({ generation: 4, requestId: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" });
    const response = await post(baseUrl, stale);
    assert.equal(response.status, 409);
    assert.equal((await response.json()).error.code, "stale_generation");
    assert.equal(upstreamCalls, 1);
  });
});

test("request validation rejects duplicate ids and invalid numeric values", async () => {
  await withGateway({ config: makeConfig(), fetchImpl: () => assert.fail("fetch must not run") }, async ({ baseUrl }) => {
    const duplicate = makePlan();
    duplicate.residents.push(structuredClone(duplicate.residents[0]));
    let response = await post(baseUrl, duplicate);
    assert.equal(response.status, 400);
    assert.equal((await response.json()).error.code, "invalid_request");

    const nonFinite = makePlan({ requestId: "11111111111111111111111111111111" });
    nonFinite.city.happiness = "NaN";
    response = await post(baseUrl, nonFinite);
    assert.equal(response.status, 400);

    const injectedConfig = makePlan({ requestId: "22222222222222222222222222222222" });
    injectedConfig.model = "unpriced/model";
    response = await post(baseUrl, injectedConfig);
    assert.equal(response.status, 400);
  });
});

test("content type, body size, and browser Origin are enforced", async () => {
  await withGateway({ config: makeConfig(), fetchImpl: () => assert.fail("fetch must not run") }, async ({ baseUrl }) => {
    let response = await fetch(`${baseUrl}/v1/citizens/plan`, { method: "POST", body: JSON.stringify(makePlan()) });
    assert.equal(response.status, 415);

    response = await fetch(`${baseUrl}/v1/citizens/plan`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ padding: "x".repeat(66_000) }),
    });
    assert.equal(response.status, 413);

    response = await fetch(`${baseUrl}/health`, { headers: { Origin: "https://evil.example" } });
    assert.equal(response.status, 403);
    assert.equal(response.headers.get("access-control-allow-origin"), null);

    response = await fetch(`${baseUrl}/health`, { headers: { Origin: "http://localhost:8080" } });
    assert.equal(response.status, 200);
    assert.equal(response.headers.get("access-control-allow-origin"), "http://localhost:8080");
  });
});

test("default rate guard permits only two new requests per minute", async () => {
  let upstreamCalls = 0;
  await withGateway({
    config: makeConfig(),
    fetchImpl: async (_url, options) => {
      upstreamCalls += 1;
      const residentId = JSON.parse(JSON.parse(options.body).messages[1].content).residents[0].id;
      return providerResponse(makePlan({ residentId }));
    },
  }, async ({ baseUrl }) => {
    for (let index = 0; index < 2; index += 1) {
      const response = await post(baseUrl, makePlan({
        generation: index + 1,
        requestId: `${index + 3}`.repeat(32),
        residentId: `resident-${index}`,
      }));
      assert.equal(response.status, 200);
    }
    const third = await post(baseUrl, makePlan({
      generation: 3,
      requestId: "55555555555555555555555555555555",
      residentId: "resident-3",
    }));
    assert.equal(third.status, 429);
    assert.equal((await third.json()).error.code, "rate_limit_exceeded");
    assert.equal(upstreamCalls, 2);
  });
});

test("cost guard rejection prevents an upstream call", async () => {
  const ledger = new FakeLedger();
  ledger.error = new GatewayError(429, "daily_budget_exceeded", "The resident AI daily cost limit has been reached.");
  await withGateway({
    config: makeConfig(),
    costLedger: ledger,
    fetchImpl: () => assert.fail("fetch must not run"),
  }, async ({ baseUrl }) => {
    const response = await post(baseUrl, makePlan());
    assert.equal(response.status, 429);
    assert.equal((await response.json()).error.code, "daily_budget_exceeded");
  });
});

test("non-loopback configuration requires client Bearer auth without leaking its token", async () => {
  const logger = testLogger();
  await withGateway({
    config: makeConfig({ host: "0.0.0.0", gatewayToken: TEST_GATEWAY_TOKEN }),
    logger,
    fetchImpl: () => assert.fail("fetch must not run"),
  }, async ({ baseUrl }) => {
    let response = await fetch(`${baseUrl}/health`);
    assert.equal(response.status, 401);
    assert.doesNotMatch(await response.text(), new RegExp(TEST_GATEWAY_TOKEN, "u"));

    response = await fetch(`${baseUrl}/health`, { headers: { Authorization: "Bearer wrong" } });
    assert.equal(response.status, 401);

    response = await fetch(`${baseUrl}/health`, { headers: { Authorization: `Bearer ${TEST_GATEWAY_TOKEN}` } });
    assert.equal(response.status, 200);
    assert.doesNotMatch(JSON.stringify(logger.events), new RegExp(TEST_GATEWAY_TOKEN, "u"));
  });
});

test("upstream timeout returns no fabricated plan and charges the conservative reservation", async () => {
  const plan = makePlan();
  await withGateway({
    config: makeConfig({ upstreamTimeoutMs: 25 }),
    fetchImpl: async (_url, options) => new Promise((_resolve, reject) => {
      options.signal.addEventListener("abort", () => reject(new DOMException("aborted", "AbortError")), { once: true });
    }),
  }, async ({ baseUrl, ledger }) => {
    const response = await post(baseUrl, plan);
    assert.equal(response.status, 504);
    const body = await response.json();
    assert.equal(body.error.code, "provider_timeout");
    assert.equal(Object.hasOwn(body, "decisions"), false);
    assert.equal(ledger.commits.length, 1);
    assert.equal(ledger.commits[0].amount, undefined);
  });
});

test("provider error bodies are never reflected to the client or logger", async () => {
  const logger = testLogger();
  await withGateway({
    config: makeConfig(),
    logger,
    fetchImpl: async () => new Response(`provider echoed ${TEST_KEY}`, { status: 400 }),
  }, async ({ baseUrl }) => {
    const response = await post(baseUrl, makePlan());
    assert.equal(response.status, 502);
    const text = await response.text();
    assert.doesNotMatch(text, new RegExp(TEST_KEY, "u"));
    assert.doesNotMatch(JSON.stringify(logger.events), new RegExp(TEST_KEY, "u"));
    assert.equal(JSON.parse(text).error.code, "provider_error");
  });
});

test("global scheduler runs one upstream job at a time and rotates waiting clients", async () => {
  const scheduler = new RoundRobinScheduler();
  const started = [];
  let active = 0;
  let maximumActive = 0;
  const gates = [];
  const job = (name) => async () => {
    started.push(name);
    active += 1;
    maximumActive = Math.max(maximumActive, active);
    await new Promise((resolve) => gates.push(resolve));
    active -= 1;
    return name;
  };

  const a1 = scheduler.enqueue("a", job("a1"));
  await new Promise((resolve) => setImmediate(resolve));
  const a2 = scheduler.enqueue("a", job("a2"));
  const b1 = scheduler.enqueue("b", job("b1"));
  gates.shift()();
  await new Promise((resolve) => setImmediate(resolve));
  assert.deepEqual(started, ["a1", "b1"]);
  gates.shift()();
  await new Promise((resolve) => setImmediate(resolve));
  assert.deepEqual(started, ["a1", "b1", "a2"]);
  gates.shift()();
  assert.deepEqual(await Promise.all([a1, a2, b1]), ["a1", "a2", "b1"]);
  assert.equal(maximumActive, 1);
});

test("configuration loads local .env safely and rejects unverified models or unsafe remote bind", async () => {
  const directory = await mkdtemp(path.join(os.tmpdir(), "riverworks-config-"));
  try {
    await writeFile(path.join(directory, ".env"), [
      `OPENROUTER_API_KEY=${TEST_KEY}`,
      "OPENROUTER_MODEL=google/gemini-2.5-flash-lite",
      "RIVERWORKS_GATEWAY_HOST=127.0.0.1",
    ].join("\n"), "utf8");
    const config = await loadConfig({ serverDir: directory, processEnvironment: {} });
    assert.equal(config.apiKey, TEST_KEY);
    assert.equal(config.host, "127.0.0.1");

    await assert.rejects(
      loadConfig({ serverDir: directory, processEnvironment: { OPENROUTER_MODEL: "unverified/model" } }),
      /price-verified/u,
    );
    await assert.rejects(
      loadConfig({ serverDir: directory, processEnvironment: { RIVERWORKS_GATEWAY_HOST: "0.0.0.0" } }),
      /requires RIVERWORKS_GATEWAY_TOKEN/u,
    );
    await assert.rejects(
      loadConfig({ serverDir: directory, processEnvironment: { RIVERWORKS_RATE_PER_MINUTE: "3" } }),
      /between 1 and 2/u,
    );
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("persistent cost ledger enforces rolling hourly and daily limits", async () => {
  const directory = await mkdtemp(path.join(os.tmpdir(), "riverworks-ledger-"));
  let current = Date.parse("2026-09-14T00:00:00.000Z");
  try {
    const ledger = new CostLedger({
      dataDir: directory,
      maxUsdPerHour: 0.001,
      maxUsdPerDay: 0.001,
      now: () => current,
    });
    const first = await ledger.reserve(0.0006);
    await ledger.commit(first, 0.0006);
    await assert.rejects(ledger.reserve(0.0005), (error) => error.code === "hourly_budget_exceeded");

    current += 60 * 60 * 1000 + 1;
    const reloaded = new CostLedger({
      dataDir: directory,
      maxUsdPerHour: 0.001,
      maxUsdPerDay: 0.001,
      now: () => current,
    });
    await assert.rejects(reloaded.reserve(0.0005), (error) => error.code === "daily_budget_exceeded");
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("corrupt persistent cost state blocks requests instead of resetting spend", async () => {
  const directory = await mkdtemp(path.join(os.tmpdir(), "riverworks-ledger-corrupt-"));
  const events = [];
  try {
    await writeFile(path.join(directory, "cost-ledger.json"), "not-json", "utf8");
    const ledger = new CostLedger({
      dataDir: directory,
      maxUsdPerHour: 0.5,
      maxUsdPerDay: 2,
      onWriteError: (event) => events.push(event),
    });
    await assert.rejects(ledger.reserve(0.001), (error) => error.code === "cost_ledger_unavailable");
    assert.deepEqual(events, ["cost_ledger_read_failed"]);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("slow provider response body is covered by the upstream timeout", async () => {
  const neverEndingBody = new ReadableStream({
    start(controller) {
      controller.enqueue(new TextEncoder().encode("{"));
    },
  });
  await withGateway({
    config: makeConfig({ upstreamTimeoutMs: 25 }),
    fetchImpl: async () => new Response(neverEndingBody, { status: 200 }),
  }, async ({ baseUrl }) => {
    const request = post(baseUrl, makePlan());
    const outcome = await Promise.race([
      request,
      new Promise((_, reject) => setTimeout(() => reject(new Error("gateway body timeout did not fire")), 500)),
    ]);
    assert.equal(outcome.status, 504);
    assert.equal((await outcome.json()).error.code, "provider_timeout");
  });
});
