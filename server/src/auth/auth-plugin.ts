import { randomBytes, timingSafeEqual } from "node:crypto";
import type { FastifyInstance, FastifyRequest } from "fastify";
import cookie from "@fastify/cookie";
import formbody from "@fastify/formbody";
import rateLimit from "@fastify/rate-limit";
import type { Pool } from "mysql2/promise";
import type { AppConfig } from "../config.js";
import type { Clock } from "../clock.js";
import { HttpError } from "../shared/http-errors.js";
import { SessionRepository, type AdminSession, type AdminIdentity } from "./session-repository.js";
import { hashPassword, verifyPassword } from "./password.js";
import { matchTotpStep } from "./totp.js";
import type { SecretBox } from "./secret-box.js";
export interface AuthDependencies {
  pool: Pool;
  config: AppConfig;
  clock: Clock;
  secretBox: SecretBox;
  sessionSecret: Buffer;
}
declare module "fastify" {
  interface FastifyRequest {
    adminSession: AdminSession | null;
    requireAdmin(): Promise<AdminIdentity>;
    verifyCsrf(): void;
  }
}
function body(request: FastifyRequest): Record<string, unknown> {
  return request.body && typeof request.body === "object" && !Array.isArray(request.body)
    ? (request.body as Record<string, unknown>)
    : {};
}
function equal(a: unknown, b: string): boolean {
  return (
    typeof a === "string" &&
    Buffer.byteLength(a) === Buffer.byteLength(b) &&
    timingSafeEqual(Buffer.from(a), Buffer.from(b))
  );
}
export async function registerAuth(app: FastifyInstance, deps: AuthDependencies): Promise<void> {
  if (deps.sessionSecret.length < 32) throw new Error("Session secret must be at least 32 bytes");
  await app.register(cookie, { secret: deps.sessionSecret });
  await app.register(formbody);
  await app.register(rateLimit, {
    global: false,
    errorResponseBuilder: () => new HttpError(429, "rate_limited")
  });
  const repository = new SessionRepository(deps.pool),
    dummy = await hashPassword(randomBytes(32).toString("hex"));
  const attributes = {
    secure: true,
    httpOnly: true,
    sameSite: "strict" as const,
    path: "/",
    signed: true
  };
  function signed(request: FastifyRequest, name: string): string | undefined {
    const value = request.cookies[name];
    if (!value) return undefined;
    const result = request.unsignCookie(value);
    return result.valid && result.value ? result.value : undefined;
  }
  app.decorateRequest("adminSession", null);
  app.decorateRequest("requireAdmin", async function (this: FastifyRequest) {
    const token = signed(this, "ps4manager_admin");
    if (!token || !/^[a-f0-9]{64}$/u.test(token))
      throw new HttpError(401, "authentication_required");
    this.adminSession = await repository.get(token, deps.clock.now());
    if (!this.adminSession) throw new HttpError(401, "authentication_required");
    return this.adminSession.actor;
  });
  app.decorateRequest("verifyCsrf", function (this: FastifyRequest) {
    if (
      !this.adminSession ||
      !equal(this.headers["x-csrf-token"] ?? body(this)._csrf, this.adminSession.csrfToken)
    )
      throw new HttpError(403, "invalid_csrf");
    if (this.headers.origin && this.headers.origin !== deps.config.publicBaseUrl.origin)
      throw new HttpError(403, "invalid_origin");
  });
  app.addHook("preHandler", async (request) => {
    const path = request.routeOptions.url;
    if (
      !(path?.startsWith("/api/admin/") || path?.startsWith("/admin/")) ||
      ["/admin/login", "/admin/admin.css", "/admin/admin.js"].includes(path) ||
      path === "/api/admin/login" ||
      path === "/api/admin/login-token"
    )
      return;
    await request.requireAdmin();
    if (!["GET", "HEAD", "OPTIONS"].includes(request.method)) request.verifyCsrf();
  });
  app.get(
    "/api/admin/login-token",
    { config: { rateLimit: { max: 30, timeWindow: "1 minute" } } },
    (_request, reply) => {
      const token = randomBytes(32).toString("hex");
      reply
        .header("Cache-Control", "no-store")
        .setCookie("ps4manager_login", `${token}:${deps.clock.now().getTime()}`, {
          ...attributes,
          maxAge: 300
        });
      return { csrfToken: token };
    }
  );
  app.post(
    "/api/admin/login",
    { bodyLimit: 4096, config: { rateLimit: { max: 5, timeWindow: "1 minute" } } },
    async (request, reply) => {
      const loginCookie = signed(request, "ps4manager_login")?.split(":"),
        values = body(request);
      const created = Number(loginCookie?.[1]),
        now = deps.clock.now();
      if (
        !loginCookie?.[0] ||
        !equal(request.headers["x-csrf-token"] ?? values._csrf, loginCookie[0]) ||
        !Number.isFinite(created) ||
        created > now.getTime() ||
        now.getTime() - created > 300_000
      )
        throw new HttpError(403, "invalid_csrf");
      if (request.headers.origin && request.headers.origin !== deps.config.publicBaseUrl.origin)
        throw new HttpError(403, "invalid_origin");
      const username = values.username,
        password = values.password,
        totp = values.totp;
      if (
        typeof username !== "string" ||
        username.length > 100 ||
        typeof password !== "string" ||
        typeof totp !== "string"
      )
        throw new HttpError(400, "invalid_request");
      const user = await repository.user(username);
      const passwordValid = await verifyPassword(password, user?.password_hash ?? dummy);
      if (!user || !passwordValid) throw new HttpError(401, "invalid_credentials");
      const totpStep = matchTotpStep(deps.secretBox.open(user.totp_secret_encrypted), totp, now, 1);
      if (totpStep === null) throw new HttpError(401, "invalid_credentials");
      const expiry = new Date(now.getTime() + deps.config.sessionTtlMinutes * 60_000);
      const session = await repository.create(
        user,
        now,
        expiry,
        totpStep,
        signed(request, "ps4manager_admin")
      );
      reply
        .header("Cache-Control", "no-store")
        .clearCookie("ps4manager_login", { path: "/" })
        .setCookie("ps4manager_admin", session.token, { ...attributes, expires: expiry });
      return { csrfToken: session.csrfToken };
    }
  );
  app.get("/api/admin/session", (request, reply) => {
    reply.header("Cache-Control", "no-store");
    return {
      username: request.adminSession!.actor.username,
      csrfToken: request.adminSession!.csrfToken
    };
  });
  app.post("/api/admin/logout", async (request, reply) => {
    await repository.remove(request.adminSession!.tokenHash);
    return reply
      .header("Cache-Control", "no-store")
      .clearCookie("ps4manager_admin", { path: "/" })
      .code(204)
      .send();
  });
}
