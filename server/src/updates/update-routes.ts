import type { FastifyInstance } from "fastify";
import { unlink } from "node:fs/promises";
import type { AppConfig } from "../config.js";
import type { ArtifactStore, StagedArtifact } from "../storage/artifact-store.js";
import { HttpError } from "../shared/http-errors.js";
import { etag } from "../shared/etag.js";
import { adminId } from "../admin/admin-repository.js";
import { UpdateService } from "./update-service.js";
export function registerUpdateRoutes(
  app: FastifyInstance,
  service: UpdateService,
  store: ArtifactStore,
  config: AppConfig
): void {
  app.get<{ Querystring: { channel?: string } }>(
    "/api/v1/updates/latest",
    async (request, reply) => {
      const result = await service.latest(request.query.channel ?? "stable"),
        tag = etag(result);
      reply.header("ETag", tag).header("Cache-Control", "no-cache");
      return request.headers["if-none-match"] === tag ? reply.code(304).send() : result;
    }
  );
  app.get<{ Params: { hash: string } }>(
    "/api/v1/updates/packages/:hash",
    async (request, reply) => {
      if (!/^[a-f0-9]{64}\.zip$/u.test(request.params.hash)) throw new HttpError(404, "not_found");
      const row = await service.repository.download(request.params.hash.slice(0, -4));
      if (!row) throw new HttpError(404, "not_found");
      store.artifactPath(row.storage_key);
      reply.hijack();
      reply.raw.writeHead(200, {
        "X-Accel-Redirect": "/_artifacts/" + row.storage_key,
        "Content-Type": "application/zip",
        "Content-Length": String(row.size_bytes),
        "Cache-Control": "public, max-age=31536000, immutable",
        ETag: `"${row.sha256}"`,
        "X-Content-Type-Options": "nosniff"
      });
      reply.raw.end();
      return reply;
    }
  );
  app.get("/api/admin/updates", async () => ({
    items: (await service.repository.list()).map((row) => ({
      id: row.id,
      state: row.state,
      ...service.publicDto(row)
    }))
  }));
  app.get("/admin/updates", async (request, reply) =>
    reply.viewAsync("update-list.ejs", {
      title: "App-Updates",
      items: await service.repository.list(),
      csrf: request.adminSession!.csrfToken
    })
  );
  app.get("/admin/updates/upload", (request, reply) =>
    reply.viewAsync("update-upload.ejs", {
      title: "Signiertes Update veröffentlichen",
      csrf: request.adminSession!.csrfToken
    })
  );
  app.post(
    "/api/admin/updates",
    { config: { rateLimit: { max: 5, timeWindow: "1 minute" } } },
    async (request, reply) => {
      let staged: StagedArtifact | undefined, manifest: unknown;
      try {
        for await (const part of request.parts({
          limits: { files: 2, fields: 0, parts: 2, fileSize: config.maxPackageBytes }
        })) {
          if (part.type !== "file") throw new HttpError(400, "invalid_upload");
          if (part.fieldname === "package" && !staged)
            staged = await store.stage(part.file, "update.zip", config.maxPackageBytes);
          else if (part.fieldname === "manifest" && manifest === undefined) {
            const chunks: Buffer[] = [];
            let size = 0;
            for await (const chunk of part.file) {
              const bytes = Buffer.from(chunk as Uint8Array);
              size += bytes.length;
              if (size > 65536) throw new HttpError(413, "payload_too_large");
              chunks.push(bytes);
            }
            try {
              manifest = JSON.parse(Buffer.concat(chunks).toString("utf8")) as unknown;
            } catch {
              throw new HttpError(400, "invalid_manifest");
            }
          } else throw new HttpError(400, "invalid_upload");
          if (part.file.truncated) throw new HttpError(413, "payload_too_large");
        }
        if (!staged || manifest === undefined) throw new HttpError(400, "invalid_upload");
        return reply
          .code(201)
          .send(await service.publish(staged, manifest, request.adminSession!.actor));
      } finally {
        if (staged) await unlink(staged.path).catch(() => undefined);
      }
    }
  );
  app.post<{ Params: { id: string }; Body: { reason?: string } }>(
    "/api/admin/updates/:id/withdraw",
    async (request) => {
      await service.withdraw(
        adminId(request.params.id),
        request.adminSession!.actor,
        request.body?.reason
      );
      return { state: "withdrawn" };
    }
  );
}
