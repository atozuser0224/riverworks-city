import { GatewayError } from "./errors.js";

export const INTENTS = Object.freeze(["home", "work", "market", "park", "square"]);
export const MOODS = Object.freeze(["calm", "happy", "worried", "curious", "tired"]);

function fail(path, detail) {
  throw new GatewayError(400, "invalid_request", `${path} ${detail}`);
}

function assertPlainObject(value, path) {
  if (value === null || typeof value !== "object" || Array.isArray(value)) {
    fail(path, "must be an object.");
  }
}

function assertExactKeys(value, keys, path) {
  assertPlainObject(value, path);
  const expected = new Set(keys);
  for (const key of Object.keys(value)) {
    if (!expected.has(key)) fail(`${path}.${key}`, "is not allowed.");
  }
  for (const key of keys) {
    if (!Object.hasOwn(value, key)) fail(`${path}.${key}`, "is required.");
  }
}

function textLength(value) {
  return [...value].length;
}

function assertText(value, path, { min = 0, max, pattern } = {}) {
  if (typeof value !== "string") fail(path, "must be a string.");
  const length = textLength(value);
  if (length < min || length > max) fail(path, `must contain ${min} to ${max} characters.`);
  if (/[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]/u.test(value)) {
    fail(path, "contains unsupported control characters.");
  }
  if (pattern && !pattern.test(value)) fail(path, "has an invalid format.");
}

function assertInteger(value, path, min, max) {
  if (!Number.isSafeInteger(value) || value < min || value > max) {
    fail(path, `must be an integer between ${min} and ${max}.`);
  }
}

function assertFinite(value, path, min, max) {
  if (!Number.isFinite(value) || value < min || value > max) {
    fail(path, `must be a finite number between ${min} and ${max}.`);
  }
}

export function validatePlanRequest(input) {
  assertExactKeys(
    input,
    ["protocol", "sessionId", "requestId", "generation", "simulationDay", "city", "residents"],
    "body",
  );
  if (input.protocol !== 1) fail("body.protocol", "must equal 1.");
  assertText(input.sessionId, "body.sessionId", {
    min: 1,
    max: 64,
    pattern: /^[A-Za-z0-9._:-]+$/u,
  });
  assertText(input.requestId, "body.requestId", {
    min: 32,
    max: 32,
    pattern: /^[A-Fa-f0-9]{32}$/u,
  });
  assertInteger(input.generation, "body.generation", 0, 2_147_483_647);
  assertInteger(input.simulationDay, "body.simulationDay", 0, 10_000_000);

  assertExactKeys(input.city, ["era", "happiness", "grain", "bread", "coins"], "body.city");
  assertText(input.city.era, "body.city.era", { min: 1, max: 40 });
  assertFinite(input.city.happiness, "body.city.happiness", 0, 100);
  assertFinite(input.city.grain, "body.city.grain", 0, 1_000_000_000);
  assertFinite(input.city.bread, "body.city.bread", 0, 1_000_000_000);
  assertFinite(input.city.coins, "body.city.coins", 0, 1_000_000_000);

  if (!Array.isArray(input.residents) || input.residents.length < 1 || input.residents.length > 16) {
    fail("body.residents", "must contain 1 to 16 residents.");
  }

  const residentIds = new Set();
  input.residents.forEach((resident, residentIndex) => {
    const base = `body.residents[${residentIndex}]`;
    assertExactKeys(
      resident,
      ["id", "name", "persona", "activity", "home", "job", "current", "allowed", "recentMemory"],
      base,
    );
    assertText(resident.id, `${base}.id`, { min: 1, max: 64, pattern: /^[A-Za-z0-9._:-]+$/u });
    if (residentIds.has(resident.id)) fail(`${base}.id`, "must be unique.");
    residentIds.add(resident.id);
    assertText(resident.name, `${base}.name`, { min: 1, max: 80 });
    assertText(resident.persona, `${base}.persona`, { min: 1, max: 400 });
    assertText(resident.activity, `${base}.activity`, { min: 1, max: 120 });
    assertInteger(resident.home, `${base}.home`, -1, 1_000_000);
    assertInteger(resident.job, `${base}.job`, -1, 1_000_000);
    assertInteger(resident.current, `${base}.current`, -1, 1_000_000);
    assertText(resident.recentMemory, `${base}.recentMemory`, { min: 0, max: 160 });

    if (!Array.isArray(resident.allowed) || resident.allowed.length < 1 || resident.allowed.length > 16) {
      fail(`${base}.allowed`, "must contain 1 to 16 choices.");
    }
    const pairs = new Set();
    resident.allowed.forEach((choice, choiceIndex) => {
      const choicePath = `${base}.allowed[${choiceIndex}]`;
      assertExactKeys(choice, ["intent", "target"], choicePath);
      if (!INTENTS.includes(choice.intent)) fail(`${choicePath}.intent`, "is not an allowed intent.");
      assertInteger(choice.target, `${choicePath}.target`, 0, 1_000_000);
      const pair = `${choice.intent}:${choice.target}`;
      if (pairs.has(pair)) fail(choicePath, "duplicates an allowed choice.");
      pairs.add(pair);
    });
  });

  return input;
}

export function validateModelDecisions(value, residents) {
  if (value === null || typeof value !== "object" || Array.isArray(value)) {
    throw new GatewayError(502, "invalid_provider_response", "The AI provider returned an invalid plan.");
  }
  if (Object.keys(value).length !== 1 || !Object.hasOwn(value, "decisions") || !Array.isArray(value.decisions)) {
    throw new GatewayError(502, "invalid_provider_response", "The AI provider returned an invalid plan.");
  }
  if (value.decisions.length !== residents.length) {
    throw new GatewayError(502, "invalid_provider_response", "The AI provider did not return one decision per resident.");
  }

  const residentsById = new Map(residents.map((resident) => [resident.id, resident]));
  const seen = new Set();
  for (const decision of value.decisions) {
    try {
      assertExactKeys(decision, ["id", "intent", "target", "dwellSeconds", "thought", "memory", "mood"], "decision");
      assertText(decision.id, "decision.id", { min: 1, max: 64 });
      if (seen.has(decision.id)) throw new Error("duplicate");
      seen.add(decision.id);
      const resident = residentsById.get(decision.id);
      if (!resident) throw new Error("unknown resident");
      if (!INTENTS.includes(decision.intent)) throw new Error("invalid intent");
      assertInteger(decision.target, "decision.target", 0, 1_000_000);
      if (!resident.allowed.some((choice) => choice.intent === decision.intent && choice.target === decision.target)) {
        throw new Error("choice not allowed");
      }
      assertInteger(decision.dwellSeconds, "decision.dwellSeconds", 3, 30);
      assertText(decision.thought, "decision.thought", { min: 0, max: 100 });
      assertText(decision.memory, "decision.memory", { min: 0, max: 160 });
      if (!MOODS.includes(decision.mood)) throw new Error("invalid mood");
    } catch {
      throw new GatewayError(502, "invalid_provider_response", "The AI provider returned a decision outside the allowed game rules.");
    }
  }

  if (seen.size !== residentsById.size) {
    throw new GatewayError(502, "invalid_provider_response", "The AI provider did not return every resident exactly once.");
  }
  return value.decisions;
}

export function stableStringify(value) {
  if (value === null || typeof value !== "object") return JSON.stringify(value);
  if (Array.isArray(value)) return `[${value.map(stableStringify).join(",")}]`;
  return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${stableStringify(value[key])}`).join(",")}}`;
}
