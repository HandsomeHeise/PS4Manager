import type { Pool, RowDataPacket } from "mysql2/promise";
import type { AdminIdentity } from "../auth/session-repository.js";
import type { Clock } from "../clock.js";
import { inTransaction } from "../db/transaction.js";
import { canonicalReleasePayload } from "../domain/release-envelope.js";
import { validateId } from "../domain/ids.js";
import { normalizeTitleId, normalizeGameVersion } from "../domain/compatibility.js";
import { ArtifactStore } from "../storage/artifact-store.js";
import { recordArtifact } from "../storage/artifact-repository.js";
import { validatePackageZip, type ZipLimits } from "../storage/zip-validator.js";
import { withStorageLock } from "../storage/storage-lock.js";
import { appendAudit } from "../shared/audit-repository.js";
import { HttpError } from "../shared/http-errors.js";
import { RsaSigner } from "./rsa-signer.js";
export interface PublishingDependencies {
  pool: Pool;
  store: ArtifactStore;
  signer: RsaSigner;
  clock: Clock;
  limits: ZipLimits;
  afterArtifactPublish?: () => Promise<void>;
}
export interface PublishModRequest {
  releaseId: string;
}
export interface PublishedRelease {
  releaseId: string;
  sha256: string;
  signature: string;
  signingKeyId: string;
}
export interface DraftRow extends RowDataPacket {
  id: string;
  mod_id: string;
  version: string;
  package_type: "files" | "luma-script";
  state: string;
  staged_path: string | null;
}
export class ModPublisher {
  constructor(private readonly deps: PublishingDependencies) {}
  async publish(request: PublishModRequest, actor: AdminIdentity): Promise<PublishedRelease> {
    validateId(request.releaseId);
    const { pool, store, signer, clock, limits } = this.deps;
    return withStorageLock(pool, (connection) =>
      inTransaction(connection, async (connection) => {
        const [rows] = await connection.query<DraftRow[]>(
          "SELECT * FROM mod_releases WHERE id=? FOR UPDATE",
          [request.releaseId]
        );
        const row = rows[0];
        if (!row) throw new HttpError(404, "not_found");
        if (row.state !== "draft" || !row.staged_path)
          throw new HttpError(409, "release_not_ready");
        const [pairs] = await connection.query<
          (RowDataPacket & { title_id: string; game_version: string })[]
        >(
          "SELECT title_id,game_version FROM release_compatibility WHERE release_id=? ORDER BY title_id,game_version",
          [row.id]
        );
        const compatibility = pairs.map((pair) => ({
          titleId: normalizeTitleId(pair.title_id),
          gameVersion: normalizeGameVersion(pair.game_version)
        }));
        if (!compatibility.length) throw new HttpError(400, "compatibility_required");
        const staged = await store.inspectStaged(row.staged_path);
        // Validate signing inputs before moving bytes into immutable storage.
        const payload = canonicalReleasePayload({
          releaseId: row.id,
          modId: row.mod_id,
          version: row.version,
          packageType: row.package_type,
          size: staged.size,
          sha256: staged.sha256,
          compatibility
        });
        let validated;
        try {
          validated = await validatePackageZip(staged.path, limits);
        } catch {
          throw new HttpError(400, "invalid_package");
        }
        if (validated.manifest.packageType !== row.package_type)
          throw new HttpError(400, "package_type_mismatch");
        const artifact = await store.publish(staged, "mods");
        await this.deps.afterArtifactPublish?.();
        const signed = await signer.sign(payload);
        const artifactId = await recordArtifact(connection, artifact, "application/zip");
        await connection.execute(
          "UPDATE mod_releases SET artifact_id=?,staged_path=NULL,manifest=?,signed_envelope=?,signature=?,signing_key_id=?,state='published',published_utc=? WHERE id=?",
          [
            artifactId,
            JSON.stringify(validated.manifest),
            payload.toString("utf8"),
            signed.signatureBase64,
            signed.keyId,
            clock.now(),
            row.id
          ]
        );
        await connection.execute("UPDATE mods SET state='published' WHERE id=?", [row.mod_id]);
        await appendAudit(connection, actor, "release.publish", row.id, clock.now(), {
          sha256: artifact.sha256
        });
        return {
          releaseId: row.id,
          sha256: artifact.sha256,
          signature: signed.signatureBase64,
          signingKeyId: signed.keyId
        };
      })
    );
  }
}
