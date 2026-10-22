import { readdir, lstat, unlink } from "node:fs/promises";
import { join, resolve } from "node:path";
import type { Pool, RowDataPacket } from "mysql2/promise";
import type { Clock } from "./clock.js";
import { ArtifactStore } from "./storage/artifact-store.js";
import { withStorageLock } from "./storage/storage-lock.js";
import { cleanupOrphansLocked } from "./storage/cleanup.js";
export async function runMaintenance(
  pool: Pool,
  store: ArtifactStore,
  clock: Clock
): Promise<void> {
  await withStorageLock(pool, async (connection) => {
    const now = clock.now();
    await connection.execute("DELETE FROM admin_sessions WHERE expires_utc<=?", [now]);
    await cleanupOrphansLocked(connection, store, now);
    const [rows] = await connection.query<(RowDataPacket & { staged_path: string })[]>(
      "SELECT staged_path FROM mod_releases WHERE state='draft' AND staged_path IS NOT NULL"
    );
    const referenced = new Set(rows.map((row) => resolve(row.staged_path))),
      directory = join(store.root, "staging");
    let entries;
    try {
      entries = await readdir(directory, { withFileTypes: true });
    } catch (error) {
      if (error instanceof Error && "code" in error && error.code === "ENOENT") return;
      throw error;
    }
    for (const entry of entries) {
      if (!entry.isFile() || !/^[a-f0-9-]{36}$/u.test(entry.name)) continue;
      const path = join(directory, entry.name);
      if (referenced.has(path)) continue;
      const stat = await lstat(path);
      if (stat.isFile() && now.getTime() - Math.max(stat.mtimeMs, stat.ctimeMs) >= 86400000)
        await unlink(path);
    }
  });
}
export function scheduleMaintenance(
  pass: () => Promise<void>,
  onError: (error: unknown) => void
): () => Promise<void> {
  let active: Promise<void> | undefined;
  const timer = setInterval(() => {
    if (active) return;
    active = pass()
      .catch(onError)
      .finally(() => {
        active = undefined;
      });
  }, 6 * 3600000);
  timer.unref();
  return async () => {
    clearInterval(timer);
    await active;
  };
}
