import { normalizeTitleId, normalizeGameVersion } from "../domain/compatibility.js";
import { validateId } from "../domain/ids.js";
import { badRequest, notFound } from "../shared/http-errors.js";
import { CatalogRepository, type ReleaseRow } from "./catalog-repository.js";
function id(value: string): string {
  try {
    return validateId(value);
  } catch {
    return badRequest();
  }
}
function integer(value: unknown, fallback: number, maximum: number): number {
  if (value === undefined) return fallback;
  if (typeof value !== "string" || !/^[1-9][0-9]*$/u.test(value)) return badRequest();
  const result = Number(value);
  if (!Number.isSafeInteger(result) || result > maximum) return badRequest();
  return result;
}
export class CatalogService {
  async media(mediaId: string) {
    const row = await this.repository.getMedia(id(mediaId));
    if (!row) return notFound();
    if (!/^previews\/[a-f0-9]{2}\/[a-f0-9]{64}\.(png|jpg|mp4)$/u.test(row.storage_key))
      throw new Error("Invalid preview key");
    return {
      storageKey: row.storage_key,
      contentType: row.content_type,
      size: row.size_bytes,
      sha256: row.sha256
    };
  }
  constructor(
    private readonly repository: CatalogRepository,
    private readonly origin: URL
  ) {}
  private url(path: string): string {
    return new URL(path, this.origin).href;
  }
  private async item(row: ReleaseRow) {
    const [pairs, media] = await Promise.all([
      this.repository.compatibility(row.id),
      this.repository.media(row.mod_id)
    ]);
    const cover = media.find((m) => m.kind === "cover");
    return {
      modId: row.mod_id,
      slug: row.slug,
      title: row.title,
      author: row.author,
      summary: row.summary,
      category: row.category,
      coverUrl: cover ? this.url(`/api/v1/media/${cover.id}`) : null,
      releaseId: row.id,
      releaseVersion: row.version,
      packageType: row.package_type,
      packageSize: row.size_bytes,
      sha256: row.sha256,
      signature: row.signature,
      signingKeyId: row.signing_key_id,
      compatibility: pairs.map((pair) => ({
        titleId: pair.titleId,
        gameVersion: pair.gameVersion
      })),
      publishedUtc: row.published_utc.toISOString(),
      detailUrl: this.url(`/api/v1/mods/${row.mod_id}`)
    };
  }
  async catalog(query: Record<string, unknown>) {
    let titleId: string, gameVersion: string;
    try {
      if (typeof query.titleId !== "string" || typeof query.gameVersion !== "string")
        return badRequest();
      titleId = normalizeTitleId(query.titleId);
      gameVersion = normalizeGameVersion(query.gameVersion);
    } catch {
      return badRequest();
    }
    const page = integer(query.page, 1, 1_000_000),
      pageSize = integer(query.pageSize, 24, 100);
    const rows = await this.repository.listCompatible(titleId, gameVersion, { page, pageSize });
    return { page, pageSize, items: await Promise.all(rows.map((row) => this.item(row))) };
  }
  async mod(modId: string) {
    const rows = await this.repository.getMod(id(modId)),
      first = rows[0];
    if (!first) return notFound();
    const media = await this.repository.media(first.mod_id);
    return {
      modId: first.mod_id,
      slug: first.slug,
      title: first.title,
      author: first.author,
      summary: first.summary,
      category: first.category,
      description: first.description,
      media: media.map((m) => ({
        kind: m.kind,
        position: m.position,
        url: this.url(`/api/v1/media/${m.id}`)
      })),
      releases: await Promise.all(rows.map((row) => this.item(row)))
    };
  }
  async release(releaseId: string) {
    const row = await this.repository.getRelease(id(releaseId));
    if (!row) return notFound();
    return {
      ...(await this.item(row)),
      state: row.state,
      withdrawalReason: row.withdrawal_reason,
      changelog: row.changelog,
      manifest: row.manifest,
      signedEnvelope: row.signed_envelope
    };
  }
  async download(releaseId: string) {
    const row = await this.repository.getRelease(id(releaseId));
    if (!row || row.state !== "published") return notFound();
    if (!/^packages\/mods\/[a-f0-9]{2}\/[a-f0-9]{64}\.zip$/u.test(row.storage_key))
      throw new Error("Invalid artifact storage key");
    return {
      storageKey: row.storage_key,
      contentType: row.content_type,
      size: row.size_bytes,
      sha256: row.sha256
    };
  }
}
