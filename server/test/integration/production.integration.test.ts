import { beforeAll, afterAll, it, expect } from "vitest";
import { mkdtemp, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { startMysql } from "../helpers/mysql.js";
import { testConfig } from "../helpers/config.js";
import { testKeys } from "../helpers/keys.js";
import { createProductionApp } from "../../src/production.js";
let db: Awaited<ReturnType<typeof startMysql>>, root: string;
beforeAll(async () => {
  db = await startMysql();
  root = await mkdtemp(join(tmpdir(), "ps4manager-production-"));
}, 240000);
afterAll(async () => {
  if (db) await db.stop();
  if (root) await rm(root, { recursive: true, force: true });
}, 60000);
it("loads configured secrets, migrates and exposes authenticated production routes", async () => {
  const keys = testKeys(),
    config = {
      ...testConfig(),
      database: db.config,
      storageRoot: join(root, "storage"),
      sessionSecretFile: join(root, "session"),
      totpEncryptionKeyFile: join(root, "totp"),
      modPrivateKeyFile: join(root, "mod"),
      updatePublicKeyFile: join(root, "update")
    };
  await writeFile(config.sessionSecretFile, Buffer.alloc(32, 1));
  await writeFile(config.totpEncryptionKeyFile, Buffer.alloc(32, 2));
  await writeFile(config.modPrivateKeyFile, keys.privateKey);
  await writeFile(config.updatePublicKeyFile, keys.publicKey);
  const app = await createProductionApp({ config, clock: { now: () => new Date() } });
  try {
    expect((await app.inject("/health/ready")).statusCode).toBe(200);
    expect((await app.inject("/admin/login")).statusCode).toBe(200);
    expect((await app.inject("/api/admin/updates")).statusCode).toBe(401);
    expect((await app.inject("/api/v1/updates/latest?channel=bogus")).statusCode).toBe(400);
    for (const [url, method, maximum, allowed] of [
      ["/api/admin/login", "POST", 5, 403],
      ["/api/admin/login-token", "GET", 30, 200]
    ] as const) {
      const call = (forwarded: string) =>
        app.inject({
          url,
          method,
          remoteAddress: "172.20.0.10",
          headers: { "x-forwarded-for": forwarded }
        });
      for (let i = 0; i < maximum; i++)
        expect((await call("198.51.100.10")).statusCode).toBe(allowed);
      expect((await call("198.51.100.10")).statusCode).toBe(429);
      // Trust only the nearest forwarded client, never attacker-supplied earlier hops.
      expect((await call("203.0.113.77, 198.51.100.10")).statusCode).toBe(429);
      expect((await call("198.51.100.11")).statusCode).toBe(allowed);
      for (let i = 0; i <= maximum; i++) {
        const direct = await app.inject({
          url,
          method,
          remoteAddress: "198.51.100.99",
          headers: { "x-forwarded-for": `203.0.113.${i + 1}` }
        });
        expect(direct.statusCode).toBe(i < maximum ? allowed : 429);
      }
    }
  } finally {
    await app.close();
  }
});
