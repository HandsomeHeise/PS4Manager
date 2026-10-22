import Fastify, { type FastifyInstance } from "fastify";
import { BlockList, isIP } from "node:net";
import type { Clock } from "./clock.js";
import type { AppConfig } from "./config.js";
import { createLoggerOptions } from "./logging.js";
import type { Pool } from "mysql2/promise";
import { CatalogRepository } from "./catalog/catalog-repository.js";
import { CatalogService } from "./catalog/catalog-service.js";
import { registerCatalogRoutes } from "./catalog/catalog-routes.js";
import { HttpError } from "./shared/http-errors.js";
import { registerAuth, type AuthDependencies } from "./auth/auth-plugin.js";
import { registerAdminRoutes, type AdminDependencies } from "./admin/admin-routes.js";
import { UpdateService } from "./updates/update-service.js";
import { registerUpdateRoutes } from "./updates/update-routes.js";
import { runMaintenance, scheduleMaintenance } from "./maintenance.js";

export interface AppDependencies {
  config: AppConfig;
  clock: Clock;
  pool?: Pool;
  closePoolOnShutdown?: boolean;
  maintenance?: boolean;
  /** Only enable behind the isolated, single-hop Nginx deployment boundary. */
  behindProxy?: boolean;
  auth?: Pick<AuthDependencies, "secretBox" | "sessionSecret">;
  publishing?: Pick<AdminDependencies, "store" | "signer">;
  updates?: { publicKey: string | Buffer };
}

export async function buildApp(deps: AppDependencies): Promise<FastifyInstance> {
  const proxyNetwork = new BlockList();
  proxyNetwork.addSubnet("10.0.0.0", 8);
  proxyNetwork.addSubnet("172.16.0.0", 12);
  proxyNetwork.addSubnet("192.168.0.0", 16);
  proxyNetwork.addSubnet("fc00::", 7, "ipv6");
  const app = Fastify({
    trustProxy: deps.behindProxy
      ? (address, hop) =>
          hop === 0 && proxyNetwork.check(address, isIP(address) === 6 ? "ipv6" : "ipv4")
      : false,
    logger: createLoggerOptions(deps.config)
  });

  app.decorate("clock", deps.clock);
  if (deps.pool && deps.closePoolOnShutdown) {
    const pool = deps.pool;
    app.addHook("onClose", async () => {
      await pool.end();
    });
  }
  if (deps.pool && deps.auth)
    await registerAuth(app, {
      ...deps.auth,
      pool: deps.pool,
      config: deps.config,
      clock: deps.clock
    });
  app.setErrorHandler((error, _request, reply) => {
    if (error instanceof HttpError) return reply.code(error.statusCode).send({ error: error.code });
    if (error instanceof Error && "code" in error && error.code === "ER_DUP_ENTRY")
      return reply.code(409).send({ error: "already_exists" });
    if (
      error instanceof Error &&
      "code" in error &&
      "statusCode" in error &&
      typeof error.code === "string" &&
      error.code.startsWith("FST_ERR_") &&
      typeof error.statusCode === "number" &&
      error.statusCode >= 400 &&
      error.statusCode < 500
    ) {
      const codes: Record<number, string> = {
        400: "invalid_request",
        413: "payload_too_large",
        415: "unsupported_media_type"
      };
      return reply
        .code(error.statusCode)
        .send({ error: codes[error.statusCode] ?? "invalid_request" });
    }
    return reply.code(500).send({ error: "internal_error" });
  });
  if (deps.publishing) {
    if (!deps.pool || !deps.auth)
      throw new Error("Publishing requires database and authentication");
    await registerAdminRoutes(app, {
      ...deps.publishing,
      pool: deps.pool,
      config: deps.config,
      clock: deps.clock
    });
  }
  if (deps.pool)
    await registerCatalogRoutes(
      app,
      new CatalogService(new CatalogRepository(deps.pool), deps.config.publicBaseUrl)
    );
  if (deps.updates) {
    if (!deps.pool || !deps.auth || !deps.publishing)
      throw new Error("Updates require authenticated publishing dependencies");
    registerUpdateRoutes(
      app,
      new UpdateService({
        pool: deps.pool,
        store: deps.publishing.store,
        publicKey: deps.updates.publicKey,
        publicBaseUrl: deps.config.publicBaseUrl,
        clock: deps.clock
      }),
      deps.publishing.store,
      deps.config
    );
  }
  app.get("/health/live", () => ({ status: "ok" }));
  app.get("/health/ready", async (_request, reply) => {
    try {
      if (!deps.pool) throw new Error("Database not configured");
      await deps.pool.query("SELECT 1");
      return { status: "ready" };
    } catch {
      return reply.code(503).send({ status: "unavailable" });
    }
  });

  if (deps.maintenance) {
    if (!deps.pool || !deps.publishing)
      throw new Error("Maintenance requires storage and database");
    const pool = deps.pool,
      store = deps.publishing.store;
    const pass = () => runMaintenance(pool, store, deps.clock);
    await pass();
    const stop = scheduleMaintenance(pass, (error) =>
      app.log.error({ err: error }, "Maintenance failed")
    );
    app.addHook("onClose", stop);
  }
  await app.ready();
  return app;
}
