import type { Pool, RowDataPacket } from "mysql2/promise";
import type { PackageManifest } from "../domain/manifest.js";
export interface PageRequest {
  page: number;
  pageSize: number;
}
export interface ReleaseRow extends RowDataPacket {
  id: string;
  mod_id: string;
  version: string;
  package_type: "files" | "luma-script";
  changelog: string;
  manifest: PackageManifest;
  signed_envelope: string;
  signature: string;
  signing_key_id: string;
  state: "published" | "withdrawn";
  withdrawal_reason: string | null;
  published_utc: Date;
  sha256: string;
  storage_key: string;
  content_type: string;
  size_bytes: number;
  slug: string;
  title: string;
  author: string;
  summary: string;
  category: string;
  description: string;
}
export interface CompatibilityRow extends RowDataPacket {
  titleId: string;
  gameVersion: string;
}
export interface MediaRow extends RowDataPacket {
  id: string;
  kind: string;
  position: number;
}
const select = `SELECT r.id,r.mod_id,r.version,r.package_type,r.changelog,r.manifest,r.signed_envelope,
 r.signature,r.signing_key_id,r.state,r.withdrawal_reason,r.published_utc,
 a.sha256,a.storage_key,a.content_type,a.size_bytes,
 m.slug,m.title,m.author,m.summary,m.category,m.description
 FROM mod_releases r JOIN mods m ON m.id=r.mod_id JOIN artifacts a ON a.id=r.artifact_id`;
export class CatalogRepository {
  constructor(private readonly pool: Pool) {}
  async getMedia(id: string) {
    const [rows] = await this.pool.query<
      (RowDataPacket & {
        storage_key: string;
        content_type: string;
        size_bytes: number;
        sha256: string;
      })[]
    >(
      `SELECT a.storage_key,a.content_type,a.size_bytes,a.sha256 FROM mod_media media
       JOIN artifacts a ON a.id=media.artifact_id JOIN mods m ON m.id=media.mod_id
       WHERE media.id=? AND m.state='published' AND EXISTS (SELECT 1 FROM mod_releases r WHERE r.mod_id=m.id AND r.state='published')`,
      [id]
    );
    return rows[0] ?? null;
  }
  async listCompatible(
    titleId: string,
    gameVersion: string,
    page: PageRequest
  ): Promise<ReleaseRow[]> {
    const [rows] = await this.pool.query<ReleaseRow[]>(
      `${select}
      WHERE r.state='published' AND m.state='published' AND EXISTS (
      SELECT 1 FROM release_compatibility c WHERE c.release_id=r.id AND c.title_id=? AND c.game_version=?)
      ORDER BY LOWER(m.title),m.id,r.published_utc DESC,r.id LIMIT ? OFFSET ?`,
      [titleId, gameVersion, page.pageSize, (page.page - 1) * page.pageSize]
    );
    return rows;
  }
  async getMod(modId: string): Promise<ReleaseRow[]> {
    const [rows] = await this.pool.query<ReleaseRow[]>(
      `${select} WHERE m.id=? AND m.state='published' AND r.state='published' ORDER BY r.published_utc DESC,r.id`,
      [modId]
    );
    return rows;
  }
  async getRelease(id: string): Promise<ReleaseRow | null> {
    const [rows] = await this.pool.query<ReleaseRow[]>(
      `${select} WHERE r.id=? AND r.state IN ('published','withdrawn') AND m.state='published'`,
      [id]
    );
    return rows[0] ?? null;
  }
  async compatibility(id: string): Promise<CompatibilityRow[]> {
    const [rows] = await this.pool.query<CompatibilityRow[]>(
      "SELECT title_id AS titleId,game_version AS gameVersion FROM release_compatibility WHERE release_id=? ORDER BY title_id,game_version",
      [id]
    );
    return rows;
  }
  async media(modId: string): Promise<MediaRow[]> {
    const [rows] = await this.pool.query<MediaRow[]>(
      "SELECT id,kind,position FROM mod_media WHERE mod_id=? ORDER BY position,id",
      [modId]
    );
    return rows;
  }
}
