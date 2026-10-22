import { describe, expect, it } from "vitest";
import { buildApp } from "../../src/app.js";
import { SystemClock } from "../../src/clock.js";
import { loadConfig } from "../../src/config.js";

const config = loadConfig({
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
});

describe("server process boundary", () => {
  it("reports liveness without requiring a database", async () => {
    const app = await buildApp({ config, clock: new SystemClock() });

    try {
      const response = await app.inject({ method: "GET", url: "/health/live" });

      expect(response.statusCode).toBe(200);
      expect(response.json()).toEqual({ status: "ok" });
    } finally {
      await app.close();
    }
  });

  it("provides the current UTC instant through the system clock", () => {
    const before = Date.now();
    const actual = new SystemClock().now();
    const after = Date.now();

    expect(actual).toBeInstanceOf(Date);
    expect(actual.getTime()).toBeGreaterThanOrEqual(before);
    expect(actual.getTime()).toBeLessThanOrEqual(after);
  });
});
