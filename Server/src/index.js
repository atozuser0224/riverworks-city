import { loadConfig } from "./config.js";
import { createGateway, safeLogger } from "./server.js";

const logger = safeLogger();

try {
  const nodeMajor = Number.parseInt(process.versions.node.split(".")[0], 10);
  if (!Number.isSafeInteger(nodeMajor) || nodeMajor < 22) throw new Error("Node.js 22 or newer is required.");
  const config = await loadConfig();
  const server = createGateway({ config, logger });
  server.listen(config.port, config.host, () => {
    logger.event("listening");
  });

  const close = () => server.close(() => process.exit(0));
  process.once("SIGINT", close);
  process.once("SIGTERM", close);
} catch {
  logger.event("startup_failed");
  process.exitCode = 1;
}
