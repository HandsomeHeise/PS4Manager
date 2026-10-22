import type { Pool, RowDataPacket } from "mysql2/promise";
import type { AdminIdentity } from "../auth/session-repository.js";
import type { Clock } from "../clock.js";
import { inTransaction } from "../db/transaction.js";
import { canonicalReleasePayload } from "../domain/release-envelope.js";
import { validateId } from "../domain/ids.js";
import { appendAudit } from "../shared/audit-repository.js";
import { HttpError } from "../shared/http-errors.js";
import { ArtifactStore } from "../storage/artifact-store.js";
import { withStorageLock } from "../storage/storage-lock.js";
import { RsaSigner } from "./rsa-signer.js";
interface ReleaseRow extends RowDataPacket {
  id: string;
  mod_id: string;
  version: string;
  package_type: "files" | "luma-script";
  state: string;
  sha256: string;
  size_bytes: number;
  storage_key: string;
  signed_envelope: string;
  signature: string;
  signing_key_id: string;
}
export class WithdrawalService {
  constructor(
    private readonly deps: { pool: Pool; store: ArtifactStore; signer: RsaSigner; clock: Clock }
  ) {}
  withdraw(releaseId: string, actor: AdminIdentity, reason: string): Promise<void> {
    if (typeof reason !== "string" || !reason.trim() || reason.length > 4000)
      throw new HttpError(400, "reason_required");
    return this.change(releaseId, actor, reason);
  }
  restore(releaseId: string, actor: AdminIdentity): Promise<void> {
    return this.change(releaseId, actor, null);
  }
  private async change(
    releaseId: string,
    actor: AdminIdentity,
    reason: string | null
  ): Promise<void> {
    validateId(releaseId);
    const { pool, store, signer, clock } = this.deps;
    await withStorageLock(pool, (connection) =>
      inTransaction(connection, async (connection) => {
        const [rows] = await connection.query<ReleaseRow[]>(
          "SELECT r.*,a.sha256,a.size_bytes,a.storage_key FROM mod_releases r JOIN artifacts a ON a.id=r.artifact_id WHERE r.id=? FOR UPDATE",
          [releaseId]
        );
        const row = rows[0];
        if (!row) throw new HttpError(404, "not_found");
        if (row.state !== (reason === null ? "withdrawn" : "published"))
          throw new HttpError(409, "invalid_release_state");
        if (reason === null) {
          try {
            const actual = await store.inspect(row.storage_key);
            const [pairs] = await connection.query<
              (RowDataPacket & { title_id: string; game_version: string })[]
            >("SELECT title_id,game_version FROM release_compatibility WHERE release_id=?", [
              row.id
            ]);
            const payload = canonicalReleasePayload({
              releaseId: row.id,
              modId: row.mod_id,
              version: row.version,
              packageType: row.package_type,
              size: actual.size,
              sha256: actual.sha256,
              compatibility: pairs.map((p) => ({
                titleId: p.title_id,
                gameVersion: p.game_version
              }))
            });
            if (
              actual.sha256 !== row.sha256 ||
              actual.size !== Number(row.size_bytes) ||
              payload.toString("utf8") !== row.signed_envelope ||
              !signer.verify(payload, row.signature, row.signing_key_id)
            )
              throw new Error("Integrity mismatch");
          } catch {
            throw new HttpError(409, "artifact_integrity_failure");
          }
        }
        await connection.execute("UPDATE mod_releases SET state=?,withdrawal_reason=? WHERE id=?", [
          reason === null ? "published" : "withdrawn",
          reason,
          row.id
        ]);
        await appendAudit(
          connection,
          actor,
          reason === null ? "release.restore" : "release.withdraw",
          row.id,
          clock.now(),
          reason === null ? {} : { reason }
        );
      })
    );
  }
}
