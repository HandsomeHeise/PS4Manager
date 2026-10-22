import { describe, expect, it } from "vitest";
import { loadConfig } from "../../src/config.js";

const valid = {
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
};

describe("loadConfig", () => {
  it("accepts an HTTPS production origin", () => {
    expect(loadConfig(valid).publicBaseUrl.href).toBe("https://mods.example.test/");
  });

  it("rejects a non-HTTPS production origin", () => {
    expect(() =>
      loadConfig({ ...valid, NODE_ENV: "production", PUBLIC_BASE_URL: "http://mods.test" })
    ).toThrow("PUBLIC_BASE_URL must use HTTPS");
  });

  it("rejects an invalid port instead of silently defaulting", () => {
    expect(() => loadConfig({ ...valid, PORT: "70000" })).toThrow("PORT");
  });

  it("applies the documented safe numeric defaults", () => {
    const config = loadConfig(valid);

    expect(config.sessionTtlMinutes).toBe(30);
    expect(config.maxPackageBytes).toBe(2_147_483_648);
    expect(config.maxExpandedBytes).toBe(4_294_967_296);
    expect(config.maxZipEntries).toBe(10_000);
    expect(config.maxPreviewBytes).toBe(52_428_800);
    expect(config.logRetentionDays).toBe(14);
  });

  it.each([
    ["SESSION_TTL_MINUTES", "0"],
    ["MAX_PACKAGE_BYTES", "-1"],
    ["MAX_EXPANDED_BYTES", "NaN"],
    ["MAX_ZIP_ENTRIES", "1.5"],
    ["MAX_PREVIEW_BYTES", "0"],
    ["LOG_RETENTION_DAYS", "3651"]
  ])("rejects an unsafe numeric value for %s", (name, value) => {
    expect(() => loadConfig({ ...valid, [name]: value })).toThrow(name);
  });

  it("exposes database settings as one boundary", () => {
    expect(loadConfig(valid).database).toEqual({
      host: "mysql",
      port: 3306,
      database: "ps4manager",
      user: "ps4manager",
      passwordFile: "/run/secrets/mysql_password"
    });
  });
});
