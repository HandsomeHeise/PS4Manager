import { it, expect } from "vitest";
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createHash } from "node:crypto";
import { execFileSync } from "node:child_process";
import { verifyArchive } from "../../backup/archive.mjs";
it("verifies every archived file and rejects changed inventory or extra data", async () => {
  const root = await mkdtemp(join(tmpdir(), "ps4manager-archive-")),
    source = join(root, "source"),
    archive = join(root, "backup.tar.gz");
  try {
    await mkdir(join(source, "artifacts"), { recursive: true });
    await writeFile(join(source, "database.sql"), "SELECT 1;");
    const item = {
      path: "database.sql",
      sha256: createHash("sha256").update("SELECT 1;").digest("hex")
    };
    await writeFile(join(source, "inventory.json"), JSON.stringify([item]));
    execFileSync("tar", ["-czf", archive, "-C", source, "."]);
    await expect(verifyArchive(archive, join(root, "good"))).resolves.toBeUndefined();
    await writeFile(join(source, "database.sql"), "SELECT 2;");
    execFileSync("tar", ["-czf", archive, "-C", source, "."]);
    await expect(verifyArchive(archive, join(root, "tampered"))).rejects.toThrow("checksum");
    await writeFile(join(source, "escape.txt"), "unlisted");
    execFileSync("tar", ["-czf", archive, "-C", source, "."]);
    await expect(verifyArchive(archive, join(root, "extra"))).rejects.toThrow(
      "Unsafe archive entry"
    );
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});
