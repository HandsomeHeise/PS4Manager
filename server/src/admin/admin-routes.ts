import type { FastifyInstance, FastifyRequest } from "fastify";
import multipart from "@fastify/multipart";
import view from "@fastify/view";
import ejs from "ejs";
import { fileURLToPath } from "node:url";
import { readFile, unlink } from "node:fs/promises";
import type { Pool } from "mysql2/promise";
import type { AppConfig } from "../config.js";
import type { Clock } from "../clock.js";
import { ArtifactStore, type StagedArtifact } from "../storage/artifact-store.js";
import { validatePreview } from "../storage/media-validator.js";
import { validatePackageZip } from "../storage/zip-validator.js";
import { RsaSigner } from "../publishing/rsa-signer.js";
import { ModPublisher } from "../publishing/mod-publisher.js";
import { WithdrawalService } from "../publishing/withdrawal-service.js";
import { HttpError, badRequest } from "../shared/http-errors.js";
import { AdminRepository, adminId } from "./admin-repository.js";
export interface AdminDependencies {
  pool: Pool;
  config: AppConfig;
  clock: Clock;
  store: ArtifactStore;
  signer: RsaSigner;
}
export async function registerAdminRoutes(
  app: FastifyInstance,
  deps: AdminDependencies
): Promise<void> {
  const { pool, config, clock, store, signer } = deps;
  const limits = {
    maxPackageBytes: config.maxPackageBytes,
    maxExpandedBytes: config.maxExpandedBytes,
    maxEntries: config.maxZipEntries,
    maxCompressionRatio: 100
  };
  const repository = new AdminRepository(pool, clock, store),
    publisher = new ModPublisher({ pool, store, signer, clock, limits }),
    withdrawal = new WithdrawalService({ pool, store, signer, clock });
  await app.register(multipart, {
    limits: { files: 1, fields: 0, parts: 1, fileSize: config.maxPackageBytes }
  });
  await app.register(view, {
    engine: { ejs },
    root: fileURLToPath(new URL("views/", import.meta.url))
  });
  app.addHook("onRequest", async (request, reply) => {
    if (request.url.startsWith("/admin/") || request.url.startsWith("/api/admin/")) {
      reply
        .header("Cache-Control", "no-store")
        .header("X-Content-Type-Options", "nosniff")
        .header(
          "Content-Security-Policy",
          "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'none"
        )
        .header("Referrer-Policy", "no-referrer");
    }
  });
  for (const [name, type] of [
    ["admin.css", "text/css"],
    ["admin.js", "application/javascript"]
  ] as const) {
    const content = await readFile(new URL(`views/${name}`, import.meta.url), "utf8");
    app.get(`/admin/${name}`, (_request, reply) => reply.type(type).send(content));
  }
  app.get("/admin/login", (_request, reply) => reply.viewAsync("login.ejs", { title: "Anmelden" }));
  app.get("/admin/mods", async (request, reply) =>
    reply.viewAsync("mod-list.ejs", {
      title: "Mod-Bibliothek",
      mods: await repository.list(),
      csrf: request.adminSession!.csrfToken
    })
  );
  app.get<{ Params: { id: string } }>("/admin/mods/:id", async (request, reply) =>
    reply.viewAsync("mod-edit.ejs", {
      title: "Mod bearbeiten",
      mod: await repository.mod(request.params.id),
      csrf: request.adminSession!.csrfToken
    })
  );
  app.get<{ Params: { id: string } }>("/admin/releases/:id", async (request, reply) =>
    reply.viewAsync("release-edit.ejs", {
      title: "Release bearbeiten",
      release: await repository.release(request.params.id),
      csrf: request.adminSession!.csrfToken
    })
  );
  app.get<{ Params: { id: string } }>("/admin/releases/:id/review", async (request, reply) => {
    const release = await repository.release(request.params.id);
    if (release.state !== "draft" || !release.staged_path)
      throw new HttpError(409, "release_not_ready");
    let validated;
    try {
      validated = await validatePackageZip(store.stagedPath(release.staged_path), limits);
    } catch {
      throw new HttpError(400, "invalid_package");
    }
    if (validated.manifest.packageType !== release.package_type)
      throw new HttpError(400, "package_type_mismatch");
    return reply.viewAsync("release-review.ejs", {
      title: "Veröffentlichung prüfen",
      release,
      manifest: validated.manifest,
      csrf: request.adminSession!.csrfToken
    });
  });
  app.post("/api/admin/mods", async (request, reply) =>
    reply.code(201).send(await repository.saveMod(request.body, request.adminSession!.actor))
  );
  app.put<{ Params: { id: string } }>("/api/admin/mods/:id", (request) =>
    repository.saveMod(request.body, request.adminSession!.actor, request.params.id)
  );
  app.post<{ Params: { id: string } }>("/api/admin/mods/:id/releases", async (request, reply) =>
    reply
      .code(201)
      .send(
        await repository.saveRelease(request.params.id, request.body, request.adminSession!.actor)
      )
  );
  app.put<{ Params: { id: string } }>("/api/admin/releases/:id", async (request) => {
    const release = await repository.release(request.params.id);
    return repository.saveRelease(
      release.mod_id,
      request.body,
      request.adminSession!.actor,
      request.params.id
    );
  });
  async function upload(request: FastifyRequest, kind: "package" | "preview") {
    let staged: StagedArtifact | undefined,
      contentType = "";
    const max = kind === "package" ? config.maxPackageBytes : config.maxPreviewBytes;
    try {
      for await (const part of request.parts({
        limits: { fileSize: max, files: 1, fields: 0, parts: 1 }
      })) {
        if (part.type !== "file" || part.fieldname !== "file" || staged) return badRequest();
        const extension =
          kind === "package"
            ? "zip"
            : (
                { "image/png": "png", "image/jpeg": "jpg", "video/mp4": "mp4" } as Record<
                  string,
                  string
                >
              )[part.mimetype];
        if (!extension) throw new HttpError(400, "invalid_preview");
        staged = await store.stage(part.file, `upload.${extension}`, max);
        contentType = part.mimetype;
        if (part.file.truncated) throw new HttpError(413, "payload_too_large");
      }
      if (!staged) return badRequest();
      return { staged, contentType };
    } catch (error) {
      if (staged) await unlink(staged.path).catch(() => undefined);
      if (error instanceof HttpError) throw error;
      if (error instanceof Error && "statusCode" in error && error.statusCode === 413)
        throw new HttpError(413, "payload_too_large");
      throw new HttpError(400, "invalid_upload");
    }
  }
  app.post<{ Params: { id: string } }>("/api/admin/releases/:id/package", async (request) => {
    const id = adminId(request.params.id),
      release = await repository.release(id);
    if (release.state !== "draft") throw new HttpError(409, "immutable_release");
    const { staged } = await upload(request, "package");
    try {
      await repository.attachPackage(id, staged, request.adminSession!.actor);
    } catch (error) {
      await unlink(staged.path).catch(() => undefined);
      throw error;
    }
    return { id, size: staged.size, sha256: staged.sha256 };
  });
  app.put<{ Params: { id: string } }>("/api/admin/mods/:id/media", (request) =>
    repository.orderMedia(request.params.id, request.body, request.adminSession!.actor)
  );
  app.post<{ Params: { id: string }; Querystring: { kind?: string; position?: string } }>(
    "/api/admin/mods/:id/media",
    async (request, reply) => {
      const mod = await repository.mod(request.params.id);
      if (mod.state !== "draft") throw new HttpError(409, "immutable_release");
      const { staged, contentType } = await upload(request, "preview");
      try {
        let metadata;
        try {
          metadata = await validatePreview(staged.path, contentType, config.maxPreviewBytes);
        } catch {
          throw new HttpError(400, "invalid_preview");
        }
        if (!request.query.position || !/^(0|[1-9][0-9]?)$/u.test(request.query.position))
          return badRequest();
        return reply
          .code(201)
          .send(
            await repository.attachMedia(
              mod.id,
              staged,
              metadata,
              request.query.kind ?? "",
              Number(request.query.position),
              request.adminSession!.actor
            )
          );
      } finally {
        await unlink(staged.path).catch(() => undefined);
      }
    }
  );
  app.post<{ Params: { id: string } }>(
    "/api/admin/releases/:id/publish",
    { config: { rateLimit: { max: 5, timeWindow: "1 minute" } } },
    (request) =>
      publisher.publish({ releaseId: adminId(request.params.id) }, request.adminSession!.actor)
  );
  app.post<{ Params: { id: string }; Body: { reason?: string } }>(
    "/api/admin/releases/:id/withdraw",
    async (request) => {
      await withdrawal.withdraw(
        adminId(request.params.id),
        request.adminSession!.actor,
        request.body?.reason ?? ""
      );
      return { state: "withdrawn" };
    }
  );
  app.post<{ Params: { id: string } }>("/api/admin/releases/:id/restore", async (request) => {
    await withdrawal.restore(adminId(request.params.id), request.adminSession!.actor);
    return { state: "published" };
  });
}
