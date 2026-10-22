import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { buildApp, type AppDependencies } from "./app.js";
import { createPool } from "./db/pool.js";
import { runMigrations } from "./db/migrate.js";
import { SecretBox } from "./auth/secret-box.js";
import { ArtifactStore } from "./storage/artifact-store.js";
import { RsaSigner } from "./publishing/rsa-signer.js";
export async function createProductionApp({
  config,
  clock
}: Pick<AppDependencies, "config" | "clock">) {
  const [sessionSecret, totpKey, privateKey, publicKey] = await Promise.all(
    [
      config.sessionSecretFile,
      config.totpEncryptionKeyFile,
      config.modPrivateKeyFile,
      config.updatePublicKeyFile
    ].map((path) => readFile(path))
  );
  const secretBox = new SecretBox(totpKey!),
    signer = new RsaSigner(privateKey!);
  const pool = createPool(config.database);
  try {
    await runMigrations(pool, fileURLToPath(new URL("../migrations/", import.meta.url)));
    return await buildApp({
      config,
      clock,
      pool,
      behindProxy: true,
      closePoolOnShutdown: true,
      maintenance: true,
      auth: { secretBox, sessionSecret: sessionSecret! },
      publishing: { store: new ArtifactStore(config.storageRoot), signer },
      updates: { publicKey: publicKey! }
    });
  } catch (error) {
    await pool.end();
    throw error;
  }
}
