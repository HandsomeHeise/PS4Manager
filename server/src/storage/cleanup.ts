import { readdir, lstat, unlink } from "node:fs/promises";
import { join } from "node:path";
import type { Pool, PoolConnection, RowDataPacket } from "mysql2/promise";
import { ArtifactStore } from "./artifact-store.js";
import { withStorageLock } from "./storage-lock.js";
// Only the immutable content-addressed namespace is eligible here. Staging
// cleanup additionally needs active-draft references and is handled separately.
export async function cleanupOrphans(
  pool: Pool,
  store: ArtifactStore,
  now: Date,
  graceMs = 86_400_000
): Promise<string[]> {
  return withStorageLock(pool, (connection) =>
    cleanupOrphansLocked(connection, store, now, graceMs)
  );
}
// Caller must hold the shared storage lock through this entire operation.
export async function cleanupOrphansLocked(
  connection: PoolConnection,
  store: ArtifactStore,
  now: Date,
  graceMs = 86_400_000
): Promise<string[]> {
  if (!Number.isFinite(now.getTime()) || !Number.isSafeInteger(graceMs) || graceMs < 86_400_000)
    throw new Error("Invalid orphan grace period");
  const [rows] = await connection.query<RowDataPacket[]>("SELECT storage_key FROM artifacts");
  const referenced = new Set(rows.map((row) => String(row.storage_key))),
    removed: string[] = [];
  for (const prefix of ["packages/mods", "packages/updates", "previews"]) {
    const base = join(store.root, prefix);
    let directories;
    try {
      directories = await readdir(base, { withFileTypes: true });
    } catch (error) {
      if (error instanceof Error && "code" in error && error.code === "ENOENT") continue;
      throw error;
    }
    for (const dir of directories) {
      if (!dir.isDirectory() || !/^[a-f0-9]{2}$/u.test(dir.name)) continue;
      for (const file of await readdir(join(base, dir.name), { withFileTypes: true })) {
        if (!file.isFile() || !/^[a-f0-9]{64}\.(?:zip|png|jpg|mp4)$/u.test(file.name)) continue;
        const key = `${prefix}/${dir.name}/${file.name}`;
        if (referenced.has(key)) continue;
        const path = store.artifactPath(key),
          stat = await lstat(path);
        if (!stat.isFile() || now.getTime() - Math.max(stat.mtimeMs, stat.ctimeMs) < graceMs)
          continue;
        await unlink(path);
        removed.push(key);
      }
    }
  }
  return removed;
}
