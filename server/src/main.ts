import { pathToFileURL } from "node:url";
import type { FastifyInstance } from "fastify";
import { type AppDependencies } from "./app.js";
import { createProductionApp } from "./production.js";
import { SystemClock } from "./clock.js";
import { loadConfig } from "./config.js";

export interface SignalSource {
  once(signal: NodeJS.Signals, listener: () => Promise<void>): unknown;
}

export type AppBuilder = (deps: AppDependencies) => Promise<FastifyInstance>;

export function registerShutdownHandlers(
  app: Pick<FastifyInstance, "close">,
  signalSource: SignalSource = process
): void {
  for (const signal of ["SIGINT", "SIGTERM"] as const) {
    signalSource.once(signal, async () => {
      await app.close();
    });
  }
}

export async function startServer(
  env: NodeJS.ProcessEnv = process.env,
  appBuilder: AppBuilder = createProductionApp
): Promise<FastifyInstance> {
  const config = loadConfig(env);
  const app = await appBuilder({ config, clock: new SystemClock() });
  registerShutdownHandlers(app);
  try {
    await app.listen({ host: config.host, port: config.port });
  } catch (error) {
    await app.close();
    throw error;
  }
  return app;
}

const entryPoint = process.argv[1] === undefined ? undefined : pathToFileURL(process.argv[1]).href;
if (entryPoint === import.meta.url) {
  startServer().catch(() => {
    process.stderr.write(
      "Server startup failed; verify configuration, secret files and database health.\n"
    );
    process.exitCode = 1;
  });
}
