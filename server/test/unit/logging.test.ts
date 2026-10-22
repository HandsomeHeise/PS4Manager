import { describe, expect, it } from "vitest";
import { loadConfig } from "../../src/config.js";
import { serializeLogRecord, createLoggerOptions } from "../../src/logging.js";
import Fastify from "fastify";

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

describe("serializeLogRecord", () => {
  it("protects the actual production logger including Fastify not-found messages", async () => {
    const output: string[] = [];
    const app = Fastify({
      logger: createLoggerOptions(
        { ...config, environment: "production" },
        { write: (value) => output.push(value) }
      )
    });
    try {
      await app.inject("/absent?token=review-query-secret");
      app.log.info({
        totpToken: "review-totp",
        nested: { PASSWORD: "review-password" },
        configuredFiles: [config.modPrivateKeyFile]
      });
      const text = output.join("");
      expect(text).not.toContain("review-query-secret");
      expect(text).not.toContain("review-totp");
      expect(text).not.toContain("review-password");
      expect(text).not.toContain(config.modPrivateKeyFile);
      expect(text).toContain('"statusCode":404');
    } finally {
      await app.close();
    }
  });
  it("keeps operational request facts while removing secrets, bodies, and query strings", () => {
    const sensitiveValues = [
      "Bearer access-token",
      "session=session-token",
      "admin-password",
      "123456",
      "csrf-secret",
      "query-secret",
      config.database.passwordFile,
      config.sessionSecretFile,
      config.totpEncryptionKeyFile,
      config.modPrivateKeyFile,
      config.updatePublicKeyFile
    ];
    const serialized = serializeLogRecord(
      {
        request: {
          method: "POST",
          url: "/api/admin/releases?token=query-secret",
          headers: {
            authorization: "Bearer access-token",
            cookie: "session=session-token",
            "set-cookie": "session=next-token"
          },
          body: { password: "admin-password", totp: "123456", _csrf: "csrf-secret" }
        },
        password: "admin-password",
        totpToken: "123456",
        csrfToken: "csrf-secret",
        configuredFiles: [
          config.database.passwordFile,
          config.sessionSecretFile,
          config.totpEncryptionKeyFile,
          config.modPrivateKeyFile,
          config.updatePublicKeyFile
        ],
        response: { statusCode: 204 },
        durationMs: 12
      },
      config
    );

    for (const sensitiveValue of sensitiveValues) {
      expect(serialized).not.toContain(sensitiveValue);
    }
    expect(serialized).not.toContain("body");
    expect(serialized).toContain('"method":"POST"');
    expect(serialized).toContain('"url":"/api/admin/releases"');
    expect(serialized).toContain('"statusCode":204');
    expect(serialized).toContain('"durationMs":12');
  });

  it("redacts sensitive keys regardless of casing or nesting", () => {
    const serialized = serializeLogRecord(
      {
        nested: {
          PASSWORD: "one",
          Totp_Value: "two",
          csrf: "three",
          Authorization: "four",
          Cookie: "five"
        }
      },
      config
    );

    expect(serialized).toBe('{"nested":{}}');
  });
});
