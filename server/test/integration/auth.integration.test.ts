import { beforeAll, afterAll, expect, it } from "vitest";
import Fastify from "fastify";
import { randomUUID } from "node:crypto";
import { fileURLToPath } from "node:url";
import { startMysql } from "../helpers/mysql.js";
import { testConfig } from "../helpers/config.js";
import { runMigrations } from "../../src/db/migrate.js";
import { hashPassword } from "../../src/auth/password.js";
import { SecretBox } from "../../src/auth/secret-box.js";
import { registerAuth } from "../../src/auth/auth-plugin.js";
import { HttpError } from "../../src/shared/http-errors.js";
import { buildApp } from "../../src/app.js";
let db: Awaited<ReturnType<typeof startMysql>>;
const app = Fastify(),
  box = new SecretBox(Buffer.alloc(32, 1));
let now = new Date(59_000),
  mutations = 0;
const password = "correct horse battery staple";
beforeAll(async () => {
  db = await startMysql();
  await runMigrations(db.pool, fileURLToPath(new URL("../../migrations", import.meta.url)));
  await db.pool.execute(
    "INSERT INTO admin_users (id,username,password_hash,totp_secret_encrypted) VALUES (?,?,?,?)",
    [
      randomUUID(),
      "admin",
      await hashPassword(password),
      box.seal(Buffer.from("12345678901234567890"))
    ]
  );
  app.setErrorHandler((error, _req, reply) =>
    reply
      .code(error instanceof HttpError ? error.statusCode : 500)
      .send({ error: error instanceof HttpError ? error.code : "internal_error" })
  );
  await registerAuth(app, {
    pool: db.pool,
    config: testConfig(),
    clock: { now: () => now },
    secretBox: box,
    sessionSecret: Buffer.alloc(32, 2)
  });
  app.post("/api/admin/probe", async (request) => {
    await request.requireAdmin();
    request.verifyCsrf();
    return { mutations: ++mutations };
  });
  await app.ready();
}, 240_000);
afterAll(async () => {
  await app.close();
  if (db) await db.stop();
}, 60_000);
async function login(
  options: {
    password?: string;
    totp?: string;
    address?: string;
    existing?: Record<string, string>;
  } = {}
) {
  const remoteAddress = options.address ?? "127.0.0.1";
  const pre = await app.inject({ url: "/api/admin/login-token", remoteAddress });
  return app.inject({
    method: "POST",
    url: "/api/admin/login",
    remoteAddress,
    cookies: {
      ...options.existing,
      ps4manager_login: pre.cookies.find((c) => c.name === "ps4manager_login")!.value
    },
    headers: { origin: "https://mods.example.test", "x-csrf-token": pre.json().csrfToken },
    payload: {
      username: "admin",
      password: options.password ?? password,
      totp: options.totp ?? "287082"
    }
  });
}
function cookies(response: Awaited<ReturnType<typeof login>>) {
  return { ps4manager_admin: response.cookies.find((c) => c.name === "ps4manager_admin")!.value };
}
it("rejects bad password, bad TOTP and login without CSRF", async () => {
  expect((await login({ password: "wrong", address: "127.0.0.2" })).statusCode).toBe(401);
  expect((await login({ totp: "000000", address: "127.0.0.3" })).statusCode).toBe(401);
  expect(
    (
      await app.inject({
        method: "POST",
        url: "/api/admin/login",
        payload: { username: "admin", password, totp: "287082" }
      })
    ).statusCode
  ).toBe(403);
});
it("uses secure cookies and blocks mutations until session and CSRF both validate", async () => {
  const result = await login();
  expect(result.statusCode).toBe(200);
  const cookie = result.cookies.find((c) => c.name === "ps4manager_admin")!;
  expect(cookie.secure).toBe(true);
  expect(cookie.httpOnly).toBe(true);
  expect(cookie.sameSite).toBe("Strict");
  expect(cookie.path).toBe("/");
  const session = cookies(result),
    csrf = result.json().csrfToken;
  expect(
    (await app.inject({ method: "POST", url: "/api/admin/probe", cookies: session })).statusCode
  ).toBe(403);
  expect(
    (
      await app.inject({
        method: "POST",
        url: "/api/admin/probe",
        cookies: session,
        headers: { "x-csrf-token": "wrong" }
      })
    ).statusCode
  ).toBe(403);
  expect(mutations).toBe(0);
  expect(
    (
      await app.inject({
        method: "POST",
        url: "/api/admin/probe",
        cookies: session,
        headers: { "x-csrf-token": csrf }
      })
    ).json().mutations
  ).toBe(1);
  const out = await app.inject({
    method: "POST",
    url: "/api/admin/logout",
    cookies: session,
    headers: { "x-csrf-token": csrf }
  });
  expect(out.statusCode).toBe(204);
  expect((await login({ address: "127.0.0.8" })).statusCode).toBe(401);
  expect(
    (
      await app.inject({
        method: "POST",
        url: "/api/admin/probe",
        cookies: session,
        headers: { "x-csrf-token": csrf }
      })
    ).statusCode
  ).toBe(401);
});
it("expires sessions and rotates an existing session on login", async () => {
  now = new Date(60_000);
  const first = await login({ address: "127.0.0.4", totp: "359152" }),
    old = cookies(first);
  now = new Date(90_000);
  const second = await login({ address: "127.0.0.4", existing: old, totp: "969429" });
  expect(second.statusCode).toBe(200);
  expect((await app.inject({ url: "/api/admin/session", cookies: old })).statusCode).toBe(401);
  now = new Date(59_000 + 31 * 60_000);
  try {
    expect(
      (await app.inject({ url: "/api/admin/session", cookies: cookies(second) })).statusCode
    ).toBe(401);
  } finally {
    now = new Date(59_000);
  }
});
it("consumes a TOTP step atomically across concurrent logins", async () => {
  now = new Date(120_000);
  const results = await Promise.all([
    login({ address: "127.0.0.10", totp: "338314" }),
    login({ address: "127.0.0.11", totp: "338314" })
  ]);
  expect(results.map((r) => r.statusCode).sort()).toEqual([200, 401]);
});
it("preserves safe framework errors in the real application", async () => {
  const real = await buildApp({
    config: testConfig(),
    clock: { now: () => now },
    pool: db.pool,
    auth: { secretBox: box, sessionSecret: Buffer.alloc(32, 2) }
  });
  try {
    for (let i = 0; i < 6; i++) {
      const result = await real.inject({
        method: "POST",
        url: "/api/admin/login",
        remoteAddress: "198.51.100.1",
        headers: { "x-forwarded-for": `203.0.113.${i + 1}` }
      });
      expect(result.statusCode).toBe(i < 5 ? 403 : 429);
    }
    for (const [payload, status, error] of [
      ['{"password":', 400, "invalid_request"],
      [JSON.stringify({ password: "x".repeat(5000) }), 413, "payload_too_large"]
    ] as const) {
      const response = await real.inject({
        method: "POST",
        url: "/api/admin/login",
        headers: { "content-type": "application/json" },
        payload
      });
      expect(response.statusCode).toBe(status);
      expect(response.json()).toEqual({ error });
    }
  } finally {
    await real.close();
  }
});
it("rate limits repeated login failures", async () => {
  let status = 0;
  for (let i = 0; i < 7; i++)
    status = (await login({ password: "wrong", address: "127.0.0.99" })).statusCode;
  expect(status).toBe(429);
});
