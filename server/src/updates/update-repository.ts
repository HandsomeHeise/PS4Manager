import type { Pool, RowDataPacket } from "mysql2/promise";
import { compareVersions, type UpdateManifest } from "./update-manifest.js";
export interface AppRelease extends RowDataPacket, UpdateManifest {
  id: string;
  state: string;
  size_bytes: number;
  published_utc: Date;
  storage_key: string;
}
export class UpdateRepository {
  constructor(private readonly pool: Pool) {}
  async list(channel?: string): Promise<AppRelease[]> {
    const [rows] = await this.pool.query<AppRelease[]>(
      `SELECT r.*,a.sha256,a.storage_key,a.size_bytes FROM app_releases r JOIN artifacts a ON a.id=r.artifact_id ${channel ? "WHERE r.channel=? AND r.state='published'" : ""}`,
      channel ? [channel] : []
    );
    return rows.sort((a, b) => compareVersions(b.version, a.version));
  }
  async download(sha256: string): Promise<AppRelease | undefined> {
    const [rows] = await this.pool.query<AppRelease[]>(
      "SELECT r.*,a.sha256,a.storage_key,a.size_bytes FROM app_releases r JOIN artifacts a ON a.id=r.artifact_id WHERE a.sha256=? AND r.state='published' LIMIT 1",
      [sha256]
    );
    return rows[0];
  }
}
