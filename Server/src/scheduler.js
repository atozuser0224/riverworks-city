export class RoundRobinScheduler {
  #queues = new Map();
  #readyClients = [];
  #active = false;
  #activeClient = null;

  enqueue(clientId, task) {
    return new Promise((resolve, reject) => {
      let queue = this.#queues.get(clientId);
      if (!queue) {
        queue = [];
        this.#queues.set(clientId, queue);
        if (clientId !== this.#activeClient) this.#readyClients.push(clientId);
      }
      queue.push({ task, resolve, reject });
      queueMicrotask(() => this.#drain());
    });
  }

  get queuedCount() {
    let total = 0;
    for (const queue of this.#queues.values()) total += queue.length;
    return total;
  }

  async #drain() {
    if (this.#active) return;
    this.#active = true;
    try {
      while (this.#readyClients.length > 0) {
        const clientId = this.#readyClients.shift();
        const queue = this.#queues.get(clientId);
        if (!queue || queue.length === 0) {
          this.#queues.delete(clientId);
          continue;
        }

        const job = queue.shift();
        this.#activeClient = clientId;
        try {
          job.resolve(await job.task());
        } catch (error) {
          job.reject(error);
        } finally {
          this.#activeClient = null;
        }

        if (queue.length > 0) this.#readyClients.push(clientId);
        else this.#queues.delete(clientId);
      }
    } finally {
      this.#active = false;
      if (this.#readyClients.length > 0) queueMicrotask(() => this.#drain());
    }
  }
}

export class SlidingWindowRateLimiter {
  #timestamps = [];

  constructor(limit, now = Date.now) {
    this.limit = limit;
    this.now = now;
  }

  take() {
    const current = this.now();
    const cutoff = current - 60_000;
    while (this.#timestamps.length > 0 && this.#timestamps[0] <= cutoff) {
      this.#timestamps.shift();
    }
    if (this.#timestamps.length >= this.limit) return false;
    this.#timestamps.push(current);
    return true;
  }
}

export class IdempotencyStore {
  #entries = new Map();

  constructor(ttlMs, now = Date.now) {
    this.ttlMs = ttlMs;
    this.now = now;
  }

  begin(key, payloadHash) {
    this.#prune();
    const existing = this.#entries.get(key);
    if (existing) {
      if (existing.payloadHash !== payloadHash) return { kind: "collision" };
      return { kind: "replay", promise: existing.promise };
    }

    let resolveEntry;
    const promise = new Promise((resolve) => {
      resolveEntry = resolve;
    });
    this.#entries.set(key, {
      payloadHash,
      promise,
      expiresAt: this.now() + this.ttlMs,
    });
    return {
      kind: "owner",
      complete: (result) => resolveEntry(result),
      promise,
    };
  }

  #prune() {
    const current = this.now();
    for (const [key, value] of this.#entries) {
      if (value.expiresAt <= current) this.#entries.delete(key);
    }
  }
}

export class GenerationTracker {
  #sessions = new Map();

  constructor(ttlMs, now = Date.now) {
    this.ttlMs = ttlMs;
    this.now = now;
  }

  accept(sessionId, generation, requestId) {
    this.#prune();
    const previous = this.#sessions.get(sessionId);
    if (previous && generation <= previous.generation && requestId !== previous.requestId) {
      return false;
    }
    if (!previous || generation > previous.generation) {
      this.#sessions.set(sessionId, {
        generation,
        requestId,
        expiresAt: this.now() + this.ttlMs,
      });
    }
    return true;
  }

  #prune() {
    const current = this.now();
    for (const [key, value] of this.#sessions) {
      if (value.expiresAt <= current) this.#sessions.delete(key);
    }
  }
}
