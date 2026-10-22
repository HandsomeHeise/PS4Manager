import type { FastifyInstance, FastifyReply, FastifyRequest } from "fastify";
import { etag } from "../shared/etag.js";
import type { CatalogService } from "./catalog-service.js";
function download(
  reply: FastifyReply,
  artifact: { storageKey: string; contentType: string; size: number; sha256: string }
) {
  // Nginx consumes this internal response and serves the bytes. Avoid Fastify
  // replacing Content-Length with zero for the intentionally empty API body.
  reply.hijack();
  reply.raw.writeHead(200, {
    "X-Accel-Redirect": "/_artifacts/" + artifact.storageKey,
    "Content-Type": artifact.contentType,
    "Content-Length": String(artifact.size),
    ETag: `"${artifact.sha256}"`,
    "Cache-Control": "public, max-age=31536000, immutable",
    "X-Content-Type-Options": "nosniff"
  });
  reply.raw.end();
  return reply;
}
function cached(request: FastifyRequest, reply: FastifyReply, value: unknown) {
  const tag = etag(value);
  reply.header("ETag", tag).header("Cache-Control", "no-cache");
  return request.headers["if-none-match"] === tag ? reply.code(304).send() : reply.send(value);
}
export function registerCatalogRoutes(
  app: FastifyInstance,
  service: CatalogService
): Promise<void> {
  app.get<{ Querystring: Record<string, unknown> }>("/api/v1/catalog", async (request, reply) =>
    cached(request, reply, await service.catalog(request.query))
  );
  app.get<{ Params: { id: string } }>("/api/v1/mods/:id", async (request, reply) =>
    cached(request, reply, await service.mod(request.params.id))
  );
  app.get<{ Params: { id: string } }>("/api/v1/releases/:id", async (request, reply) =>
    cached(request, reply, await service.release(request.params.id))
  );
  app.get<{ Params: { id: string } }>("/api/v1/releases/:id/download", async (request, reply) => {
    const artifact = await service.download(request.params.id);
    return download(reply, artifact);
  });
  app.get<{ Params: { id: string } }>("/api/v1/media/:id", async (request, reply) =>
    download(reply, await service.media(request.params.id))
  );
  return Promise.resolve();
}
