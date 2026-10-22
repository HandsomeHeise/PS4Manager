import type { FastifyInstance } from "fastify";
import { describe, expect, it, vi } from "vitest";
import { registerShutdownHandlers, startServer } from "../../src/main.js";

describe("server lifecycle", () => {
  it("closes Fastify for SIGINT and SIGTERM", async () => {
    const close = vi.fn(async () => undefined);
    const handlers = new Map<NodeJS.Signals, () => Promise<void>>();
    const signals = {
      once(signal: NodeJS.Signals, handler: () => Promise<void>) {
        handlers.set(signal, handler);
        return signals;
      }
    };

    registerShutdownHandlers({ close } as unknown as FastifyInstance, signals);

    expect([...handlers.keys()]).toEqual(["SIGINT", "SIGTERM"]);
    await handlers.get("SIGTERM")?.();
    expect(close).toHaveBeenCalledOnce();
  });

  it("propagates startup failures", async () => {
    const startupError = new Error("listen failed");
    const app = {
      listen: vi.fn(async () => Promise.reject(startupError)),
      close: vi.fn(async () => undefined)
    } as unknown as FastifyInstance;

    await expect(
      startServer(
        {
          NODE_ENV: "test",
          HOST: "127.0.0.1",
          PORT: "3100",
          PUBLIC_BASE_URL: "https://mods.example.test",
          MYSQL_HOST: "mysql",
          MYSQL_PORT: "3306",
          MYSQL_DATABASE: "ps4manager",
          MYSQL_USER: "ps4manager",
          MYSQL_PASSWORD_FILE: "/run/secrets/mysql_password",
          STORAGE_ROOT: "/var/lib/ps4manager",
          SESSION_SECRET_FILE: "/run/secrets/session_secret",
          TOTP_ENCRYPTION_KEY_FILE: "/run/secrets/totp_key",
          MOD_PRIVATE_KEY_FILE: "/run/secrets/mod_private_key",
          UPDATE_PUBLIC_KEY_FILE: "/run/secrets/update_public_key"
        },
        async () => app
      )
    ).rejects.toBe(startupError);
  });
});
