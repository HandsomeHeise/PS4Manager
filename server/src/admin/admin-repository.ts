import { randomUUID } from "node:crypto";
import type { Pool, PoolConnection, RowDataPacket } from "mysql2/promise";
import type { AdminIdentity } from "../auth/session-repository.js";
import type { Clock } from "../clock.js";
import { withTransaction, inTransaction } from "../db/transaction.js";
import { normalizeTitleId, normalizeGameVersion } from "../domain/compatibility.js";
import { validateId } from "../domain/ids.js";
import { appendAudit } from "../shared/audit-repository.js";
import { HttpError, badRequest } from "../shared/http-errors.js";
import { withStorageLock } from "../storage/storage-lock.js";
import { recordArtifact } from "../storage/artifact-repository.js";
import type { ArtifactStore, StagedArtifact } from "../storage/artifact-store.js";
import type { PreviewMetadata } from "../storage/media-validator.js";
export interface ModRecord extends RowDataPacket {
  id: string;
  slug: string;
  title: string;
  author: string;
  summary: string;
  description: string;
  category: string;
  state: string;
}
export interface ReleaseRecord extends RowDataPacket {
  id: string;
  mod_id: string;
  title: string;
  version: string;
  author: string;
  summary: string;
  description: string;
  category: string;
  package_type: string;
  changelog: string;
  state: string;
  staged_path: string | null;
}
interface Pair extends RowDataPacket {
  titleId: string;
  gameVersion: string;
}
export function adminId(value: string): string {
  try {
    return validateId(value);
  } catch {
    return badRequest();
  }
}
function object(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value)) return badRequest();
  return value as Record<string, unknown>;
}
function text(value: unknown, max: number): string {
  if (typeof value !== "string" || !value.trim() || value.length > max) return badRequest();
  return value.trim();
}
function modFields(input: unknown) {
  const v = object(input),
    slug = text(v.slug, 160);
  if (!/^[a-z0-9]+(?:-[a-z0-9]+)*$/u.test(slug)) return badRequest();
  return [
    slug,
    text(v.title, 255),
    text(v.author, 255),
    text(v.summary, 1000),
    text(v.description, 16000),
    text(v.category, 100)
  ];
}
function releaseFields(input: unknown) {
  const v = object(input),
    version = text(v.version, 100),
    packageType = v.packageType;
  if (
    !/^[0-9A-Za-z][0-9A-Za-z.+-]{0,99}$/u.test(version) ||
    (packageType !== "files" && packageType !== "luma-script") ||
    !Array.isArray(v.compatibility) ||
    !v.compatibility.length ||
    v.compatibility.length > 1000
  )
    return badRequest();
  const pairs: { titleId: string; gameVersion: string }[] = [];
  try {
    for (const item of v.compatibility) {
      const pair = object(item);
      pairs.push({
        titleId: normalizeTitleId(pair.titleId as string),
        gameVersion: normalizeGameVersion(pair.gameVersion as string)
      });
    }
  } catch {
    return badRequest();
  }
  if (new Set(pairs.map((p) => `${p.titleId}@${p.gameVersion}`)).size !== pairs.length)
    return badRequest();
  return { version, packageType, changelog: text(v.changelog, 16000), pairs };
}
async function lockDraft(connection: PoolConnection, table: "mods" | "mod_releases", id: string) {
  const [rows] = await connection.query<RowDataPacket[]>(
    `SELECT * FROM ${table} WHERE id=? FOR UPDATE`,
    [adminId(id)]
  );
  if (!rows[0]) throw new HttpError(404, "not_found");
  if (rows[0].state !== "draft") throw new HttpError(409, "immutable_release");
  return rows[0];
}
export class AdminRepository {
  constructor(
    private readonly pool: Pool,
    private readonly clock: Clock,
    private readonly store: ArtifactStore
  ) {}
  async list() {
    const [rows] = await this.pool.query<RowDataPacket[]>(
      "SELECT id,title,state FROM mods ORDER BY created_utc DESC,id LIMIT 500"
    );
    return rows;
  }
  async mod(id: string) {
    const [rows] = await this.pool.query<ModRecord[]>("SELECT * FROM mods WHERE id=?", [
      adminId(id)
    ]);
    if (!rows[0]) throw new HttpError(404, "not_found");
    const [releases] = await this.pool.query<RowDataPacket[]>(
      "SELECT id,version,state FROM mod_releases WHERE mod_id=? ORDER BY created_utc DESC,id",
      [id]
    );
    const [media] = await this.pool.query<RowDataPacket[]>(
      "SELECT id,kind,position FROM mod_media WHERE mod_id=? ORDER BY position",
      [id]
    );
    return { ...rows[0], releases, media };
  }
  async release(id: string) {
    const [rows] = await this.pool.query<ReleaseRecord[]>(
      "SELECT r.*,m.title,m.author,m.summary,m.description,m.category FROM mod_releases r JOIN mods m ON m.id=r.mod_id WHERE r.id=?",
      [adminId(id)]
    );
    if (!rows[0]) throw new HttpError(404, "not_found");
    const [pairs] = await this.pool.query<Pair[]>(
      "SELECT title_id AS titleId,game_version AS gameVersion FROM release_compatibility WHERE release_id=? ORDER BY title_id,game_version",
      [id]
    );
    return { ...rows[0], compatibility: pairs };
  }
  async saveMod(input: unknown, actor: AdminIdentity, existingId?: string) {
    const fields = modFields(input),
      id = existingId ? adminId(existingId) : randomUUID();
    await withTransaction(this.pool, async (connection) => {
      if (existingId) {
        await lockDraft(connection, "mods", id);
        await connection.execute(
          "UPDATE mods SET slug=?,title=?,author=?,summary=?,description=?,category=? WHERE id=?",
          [...fields, id]
        );
      } else
        await connection.execute(
          "INSERT INTO mods (id,slug,title,author,summary,description,category) VALUES (?,?,?,?,?,?,?)",
          [id, ...fields]
        );
      await appendAudit(
        connection,
        actor,
        existingId ? "mod.edit" : "mod.create",
        id,
        this.clock.now()
      );
    });
    return { id };
  }
  async saveRelease(modId: string, input: unknown, actor: AdminIdentity, existingId?: string) {
    const fields = releaseFields(input),
      id = existingId ? adminId(existingId) : randomUUID();
    adminId(modId);
    await withTransaction(this.pool, async (connection) => {
      if (existingId) {
        const row = await lockDraft(connection, "mod_releases", id);
        if (row.mod_id !== modId) throw new HttpError(404, "not_found");
        await connection.execute(
          "UPDATE mod_releases SET version=?,package_type=?,changelog=? WHERE id=?",
          [fields.version, fields.packageType, fields.changelog, id]
        );
        await connection.execute("DELETE FROM release_compatibility WHERE release_id=?", [id]);
      } else {
        const [mods] = await connection.query<RowDataPacket[]>(
          "SELECT id FROM mods WHERE id=? AND state!='archived' FOR UPDATE",
          [modId]
        );
        if (!mods.length) throw new HttpError(404, "not_found");
        await connection.execute(
          "INSERT INTO mod_releases (id,mod_id,version,package_type,changelog) VALUES (?,?,?,?,?)",
          [id, modId, fields.version, fields.packageType, fields.changelog]
        );
      }
      for (const pair of fields.pairs)
        await connection.execute(
          "INSERT INTO release_compatibility (id,release_id,title_id,game_version) VALUES (?,?,?,?)",
          [randomUUID(), id, pair.titleId, pair.gameVersion]
        );
      await appendAudit(
        connection,
        actor,
        existingId ? "release.edit" : "release.create",
        id,
        this.clock.now()
      );
    });
    return { id };
  }
  async attachPackage(id: string, staged: StagedArtifact, actor: AdminIdentity) {
    await withTransaction(this.pool, async (connection) => {
      await lockDraft(connection, "mod_releases", id);
      await connection.execute("UPDATE mod_releases SET staged_path=? WHERE id=?", [
        staged.path,
        id
      ]);
      await appendAudit(connection, actor, "release.upload", id, this.clock.now(), {
        sha256: staged.sha256,
        size: staged.size
      });
    });
  }
  async orderMedia(modId: string, input: unknown, actor: AdminIdentity) {
    const order = object(input).order;
    if (
      !Array.isArray(order) ||
      order.length > 100 ||
      order.some((id) => typeof id !== "string") ||
      new Set(order).size !== order.length
    )
      return badRequest();
    const ids = order as string[];
    await withTransaction(this.pool, async (connection) => {
      await lockDraft(connection, "mods", modId);
      const [rows] = await connection.query<(RowDataPacket & { id: string })[]>(
        "SELECT id FROM mod_media WHERE mod_id=?",
        [modId]
      );
      if (rows.length !== ids.length || rows.some((row) => !ids.includes(row.id)))
        return badRequest();
      // Reserve a disjoint temporary range to avoid unique-position collisions.
      await connection.execute("UPDATE mod_media SET position=position+100 WHERE mod_id=?", [
        modId
      ]);
      for (const [position, id] of ids.entries())
        await connection.execute("UPDATE mod_media SET position=? WHERE id=? AND mod_id=?", [
          position,
          id,
          modId
        ]);
      await appendAudit(connection, actor, "media.reorder", modId, this.clock.now(), {
        order: ids
      });
    });
    return { id: modId };
  }
  async attachMedia(
    modId: string,
    staged: StagedArtifact,
    metadata: PreviewMetadata,
    kind: string,
    position: number,
    actor: AdminIdentity
  ) {
    if (
      !["cover", "screenshot", "video"].includes(kind) ||
      !Number.isInteger(position) ||
      position < 0 ||
      position > 99 ||
      (kind === "video") !== (metadata.extension === "mp4")
    )
      return badRequest();
    const id = randomUUID();
    await withStorageLock(this.pool, (connection) =>
      inTransaction(connection, async (connection) => {
        await lockDraft(connection, "mods", modId);
        if (kind === "cover") {
          const [covers] = await connection.query<RowDataPacket[]>(
            "SELECT id FROM mod_media WHERE mod_id=? AND kind='cover'",
            [modId]
          );
          if (covers.length) throw new HttpError(409, "cover_exists");
        }
        const artifact = await this.store.publish(
          { ...staged, extension: metadata.extension },
          "previews"
        );
        const artifactId = await recordArtifact(connection, artifact, metadata.contentType);
        await connection.execute(
          "INSERT INTO mod_media (id,mod_id,artifact_id,kind,position) VALUES (?,?,?,?,?)",
          [id, modId, artifactId, kind, position]
        );
        await appendAudit(connection, actor, "media.upload", modId, this.clock.now(), {
          mediaId: id,
          kind,
          position
        });
      })
    );
    return { id };
  }
}
