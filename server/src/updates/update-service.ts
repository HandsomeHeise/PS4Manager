import { constants, createPublicKey, verify, randomUUID, type KeyObject } from "node:crypto";
import type { Pool, RowDataPacket } from "mysql2/promise";
import type { Clock } from "../clock.js";
import type { AdminIdentity } from "../auth/session-repository.js";
import { inTransaction, withTransaction } from "../db/transaction.js";
import { ArtifactStore, type StagedArtifact } from "../storage/artifact-store.js";
import { withStorageLock } from "../storage/storage-lock.js";
import { recordArtifact } from "../storage/artifact-repository.js";
import { appendAudit } from "../shared/audit-repository.js";
import { HttpError } from "../shared/http-errors.js";
import { parseUpdateManifest, signedUpdatePayload } from "./update-manifest.js";
import { UpdateRepository, type AppRelease } from "./update-repository.js";
export interface UpdateDependencies {
  pool: Pool;
  store: ArtifactStore;
  publicKey: string | Buffer;
  publicBaseUrl: URL;
  clock: Clock;
}
export class UpdateService {
  private readonly key: KeyObject;
  readonly repository: UpdateRepository;
  constructor(private readonly deps: UpdateDependencies) {
    this.key = createPublicKey(deps.publicKey);
    if (
      this.key.asymmetricKeyType !== "rsa" ||
      (this.key.asymmetricKeyDetails?.modulusLength ?? 0) < 2048
    )
      throw new Error("Invalid update public key");
    this.repository = new UpdateRepository(deps.pool);
  }
  publicDto(row: AppRelease) {
    return {
      version: row.version,
      channel: row.channel,
      url: row.url,
      sha256: row.sha256,
      notes: row.notes,
      signature: row.signature,
      size: Number(row.size_bytes),
      publishedUtc: row.published_utc.toISOString()
    };
  }
  async latest(channel: unknown) {
    if (channel !== "stable" && channel !== "preview") throw new HttpError(400, "invalid_channel");
    const row = (await this.repository.list(channel))[0];
    if (!row) throw new HttpError(404, "not_found");
    return this.publicDto(row);
  }
  async publish(staged: StagedArtifact, input: unknown, actor: AdminIdentity) {
    const manifest = parseUpdateManifest(input),
      payload = signedUpdatePayload(manifest);
    const actual = await this.deps.store.inspectStaged(staged.path);
    const expected = new URL(
      `/api/v1/updates/packages/${actual.sha256}.zip`,
      this.deps.publicBaseUrl
    ).href;
    if (
      actual.sha256 !== manifest.sha256 ||
      manifest.url !== expected ||
      !verify(
        "RSA-SHA256",
        payload,
        { key: this.key, padding: constants.RSA_PKCS1_PADDING },
        Buffer.from(manifest.signature, "base64")
      )
    )
      throw new HttpError(400, "invalid_update_signature");
    const id = randomUUID(),
      { pool, store, clock } = this.deps;
    await withStorageLock(pool, (connection) =>
      inTransaction(connection, async (connection) => {
        const [existing] = await connection.query<RowDataPacket[]>(
          "SELECT id FROM app_releases WHERE channel=? AND version=?",
          [manifest.channel, manifest.version]
        );
        if (existing.length) throw new HttpError(409, "already_exists");
        const artifact = await store.publish(actual, "updates"),
          artifactId = await recordArtifact(connection, artifact, "application/zip");
        await connection.execute(
          "INSERT INTO app_releases (id,version,channel,notes,artifact_id,url,signature,signed_envelope,state,published_utc) VALUES (?,?,?,?,?,?,?,?,'published',?)",
          [
            id,
            manifest.version,
            manifest.channel,
            manifest.notes,
            artifactId,
            manifest.url,
            manifest.signature,
            payload.toString("utf8"),
            clock.now()
          ]
        );
        await appendAudit(connection, actor, "update.publish", id, clock.now(), {
          version: manifest.version,
          channel: manifest.channel,
          sha256: actual.sha256
        });
      })
    );
    return { id };
  }
  async withdraw(id: string, actor: AdminIdentity, reason: unknown): Promise<void> {
    if (typeof reason !== "string" || !reason.trim() || reason.length > 4000)
      throw new HttpError(400, "reason_required");
    await withTransaction(this.deps.pool, async (connection) => {
      const [rows] = await connection.query<RowDataPacket[]>(
        "SELECT state FROM app_releases WHERE id=? FOR UPDATE",
        [id]
      );
      if (!rows.length) throw new HttpError(404, "not_found");
      if (rows[0]!.state !== "published") throw new HttpError(409, "invalid_release_state");
      await connection.execute("UPDATE app_releases SET state='withdrawn' WHERE id=?", [id]);
      await appendAudit(connection, actor, "update.withdraw", id, this.deps.clock.now(), {
        reason
      });
    });
  }
}
