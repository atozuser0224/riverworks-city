import { mkdir, readFile, rename, writeFile } from "node:fs/promises";
import path from "node:path";
import { randomUUID } from "node:crypto";
import { GatewayError } from "./errors.js";

const HOUR_MS = 60 * 60 * 1000;
const DAY_MS = 24 * HOUR_MS;

export class CostLedger {
  #entries = [];
  #reservations = new Map();
  #loadPromise;
  #unavailable = false;

  constructor({ dataDir, maxUsdPerHour, maxUsdPerDay, now = Date.now, onWriteError = () => {} }) {
    this.dataDir = dataDir;
    this.filePath = path.join(dataDir, "cost-ledger.json");
    this.maxUsdPerHour = maxUsdPerHour;
    this.maxUsdPerDay = maxUsdPerDay;
    this.now = now;
    this.onWriteError = onWriteError;
    this.#loadPromise = this.#load();
  }

  async reserve(maxEstimatedUsd) {
    await this.#loadPromise;
    if (this.#unavailable) {
      throw new GatewayError(503, "cost_ledger_unavailable", "The resident AI cost ledger is unavailable.");
    }
    this.#prune();
    const current = this.now();
    const hourSpent = this.#entries.filter((entry) => entry.at > current - HOUR_MS).reduce((sum, entry) => sum + entry.usd, 0);
    const daySpent = this.#entries.reduce((sum, entry) => sum + entry.usd, 0);
    const reserved = [...this.#reservations.values()].reduce((sum, value) => sum + value, 0);

    if (hourSpent + reserved + maxEstimatedUsd > this.maxUsdPerHour + Number.EPSILON) {
      throw new GatewayError(429, "hourly_budget_exceeded", "The resident AI hourly cost limit has been reached.");
    }
    if (daySpent + reserved + maxEstimatedUsd > this.maxUsdPerDay + Number.EPSILON) {
      throw new GatewayError(429, "daily_budget_exceeded", "The resident AI daily cost limit has been reached.");
    }

    const id = randomUUID();
    this.#reservations.set(id, maxEstimatedUsd);
    return { id, maxEstimatedUsd };
  }

  async commit(reservation, actualUsd) {
    await this.#loadPromise;
    if (!this.#reservations.delete(reservation.id)) return;
    const safeCost = Number.isFinite(actualUsd) && actualUsd >= 0
      ? actualUsd
      : reservation.maxEstimatedUsd;
    this.#entries.push({ at: this.now(), usd: safeCost });
    this.#prune();
    await this.#persist();
  }

  release(reservation) {
    this.#reservations.delete(reservation.id);
  }

  async #load() {
    try {
      const parsed = JSON.parse(await readFile(this.filePath, "utf8"));
      if (
        parsed?.version !== 1 ||
        !Array.isArray(parsed.entries) ||
        !parsed.entries.every(
          (entry) => Number.isFinite(entry?.at) && Number.isFinite(entry?.usd) && entry.usd >= 0,
        )
      ) throw new Error("invalid cost ledger");
      this.#entries = parsed.entries;
    } catch (error) {
      if (error?.code !== "ENOENT") {
        this.#unavailable = true;
        this.onWriteError("cost_ledger_read_failed");
      }
    }
    this.#prune();
  }

  #prune() {
    const cutoff = this.now() - DAY_MS;
    this.#entries = this.#entries.filter((entry) => entry.at > cutoff);
  }

  async #persist() {
    try {
      await mkdir(this.dataDir, { recursive: true });
      const temporary = `${this.filePath}.${process.pid}.tmp`;
      await writeFile(temporary, JSON.stringify({ version: 1, entries: this.#entries }), { encoding: "utf8", mode: 0o600 });
      await rename(temporary, this.filePath);
    } catch {
      this.#unavailable = true;
      this.onWriteError("cost_ledger_write_failed");
    }
  }
}
