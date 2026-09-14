import { readFile } from "node:fs/promises";
import { isIP } from "node:net";
import path from "node:path";

export const MODEL_CATALOG = Object.freeze({
  "google/gemini-2.5-flash-lite": Object.freeze({
    inputUsdPerMillion: 0.10,
    outputUsdPerMillion: 0.40,
  }),
});

function parseDotEnv(contents) {
  const values = {};
  for (const sourceLine of contents.replace(/^\uFEFF/, "").split(/\r?\n/u)) {
    const line = sourceLine.trim();
    if (!line || line.startsWith("#")) continue;
    const match = /^(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$/u.exec(line);
    if (!match) continue;

    let value = match[2].trim();
    if (
      value.length >= 2 &&
      ((value.startsWith('"') && value.endsWith('"')) ||
        (value.startsWith("'") && value.endsWith("'")))
    ) {
      const quote = value[0];
      value = value.slice(1, -1);
      if (quote === '"') {
        value = value.replace(/\\n/gu, "\n").replace(/\\r/gu, "\r").replace(/\\"/gu, '"').replace(/\\\\/gu, "\\");
      }
    } else {
      value = value.replace(/\s+#.*$/u, "").trim();
    }
    values[match[1]] = value;
  }
  return values;
}

async function readLocalEnvironment(serverDir) {
  try {
    return parseDotEnv(await readFile(path.join(serverDir, ".env"), "utf8"));
  } catch (error) {
    if (error?.code === "ENOENT") return {};
    throw new Error("Unable to read Server/.env.");
  }
}

function integerSetting(env, name, fallback, min, max) {
  const raw = env[name];
  if (raw === undefined || raw === "") return fallback;
  if (!/^\d+$/u.test(raw)) throw new Error(`${name} must be an integer.`);
  const value = Number(raw);
  if (!Number.isSafeInteger(value) || value < min || value > max) {
    throw new Error(`${name} must be between ${min} and ${max}.`);
  }
  return value;
}

function decimalSetting(env, name, fallback, min, max) {
  const raw = env[name];
  if (raw === undefined || raw === "") return fallback;
  const value = Number(raw);
  if (!Number.isFinite(value) || value < min || value > max) {
    throw new Error(`${name} must be between ${min} and ${max}.`);
  }
  return value;
}

export function isLoopbackHost(host) {
  const normalized = host.toLowerCase().replace(/^\[|\]$/gu, "");
  if (normalized === "localhost" || normalized === "::1") return true;
  if (isIP(normalized) === 4) return normalized.startsWith("127.");
  return false;
}

export async function loadConfig({
  serverDir = path.resolve(import.meta.dirname, ".."),
  processEnvironment = process.env,
} = {}) {
  const fileEnvironment = await readLocalEnvironment(serverDir);
  const env = { ...fileEnvironment, ...processEnvironment };
  const host = env.RIVERWORKS_GATEWAY_HOST || "127.0.0.1";
  const port = integerSetting(env, "RIVERWORKS_GATEWAY_PORT", 47841, 1, 65535);
  const model = env.OPENROUTER_MODEL || "google/gemini-2.5-flash-lite";
  const pricing = MODEL_CATALOG[model];

  if (!pricing) {
    throw new Error("OPENROUTER_MODEL is not in the price-verified model catalog.");
  }

  const gatewayToken = env.RIVERWORKS_GATEWAY_TOKEN || "";
  if (!isLoopbackHost(host) && gatewayToken.length < 16) {
    throw new Error("A non-loopback gateway host requires RIVERWORKS_GATEWAY_TOKEN with at least 16 characters.");
  }

  return Object.freeze({
    serverDir,
    dataDir: path.join(serverDir, ".data"),
    host,
    port,
    gatewayToken,
    apiKey: (env.OPENROUTER_API_KEY || "").trim(),
    model,
    pricing,
    provider: "openrouter",
    upstreamUrl: "https://openrouter.ai/api/v1/chat/completions",
    ratePerMinute: integerSetting(env, "RIVERWORKS_RATE_PER_MINUTE", 2, 1, 2),
    maxUsdPerHour: decimalSetting(env, "RIVERWORKS_MAX_USD_PER_HOUR", 0.5, 0.01, 0.5),
    maxUsdPerDay: decimalSetting(env, "RIVERWORKS_MAX_USD_PER_DAY", 2, 0.01, 2),
    upstreamTimeoutMs: integerSetting(env, "RIVERWORKS_UPSTREAM_TIMEOUT_MS", 30_000, 1_000, 30_000),
    maxBodyBytes: 64 * 1024,
    idempotencyTtlMs: 120_000,
    maxOutputTokens: 4_096,
    temperature: 0.6,
  });
}
