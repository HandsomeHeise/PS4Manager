import { randomUUID } from "node:crypto";
import type { PoolConnection, RowDataPacket } from "mysql2/promise";
import type { StoredArtifact } from "./artifact-store.js";
export async function recordArtifact(
  connection: PoolConnection,
  artifact: StoredArtifact,
  contentType: string
): Promise<string> {
  await connection.execute(
    "INSERT INTO artifacts (id,sha256,storage_key,content_type,size_bytes) VALUES (?,?,?,?,?) ON DUPLICATE KEY UPDATE id=id",
    [randomUUID(), artifact.sha256, artifact.storageKey, contentType, artifact.size]
  );
  const [rows] = await connection.query<RowDataPacket[]>(
    "SELECT id,storage_key,content_type,size_bytes FROM artifacts WHERE sha256=?",
    [artifact.sha256]
  );
  const row = rows[0];
  if (
    !row ||
    row.storage_key !== artifact.storageKey ||
    row.content_type !== contentType ||
    Number(row.size_bytes) !== artifact.size
  )
    throw new Error("Artifact metadata conflict");
  return String(row.id);
}
