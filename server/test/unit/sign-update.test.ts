import { it, expect } from "vitest";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { mkdtemp, writeFile, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createHash, verify, constants } from "node:crypto";
import { testKeys } from "../helpers/keys.js";
it("signs an offline manifest without overwriting an existing output", async () => {
  const directory = await mkdtemp(join(tmpdir(), "ps4manager-sign-")),
    keys = testKeys();
  try {
    await writeFile(join(directory, "key.pem"), keys.privateKey);
    await writeFile(join(directory, "package.zip"), "fixture package");
    await writeFile(join(directory, "notes.txt"), "Notes ä\nsecond line");
    const args = [
      "node_modules/tsx/dist/cli.mjs",
      "scripts/sign-update.ts",
      "--version",
      "1.2.0",
      "--channel",
      "stable",
      "--url",
      "https://mods.example.test/api/v1/updates/packages/{sha256}.zip",
      "--notes-file",
      join(directory, "notes.txt"),
      "--package",
      join(directory, "package.zip"),
      "--private-key",
      join(directory, "key.pem"),
      "--out",
      join(directory, "manifest.json")
    ];
    await promisify(execFile)(process.execPath, args);
    const manifest = JSON.parse(await readFile(join(directory, "manifest.json"), "utf8"));
    const hash = createHash("sha256").update("fixture package").digest("hex");
    expect(manifest.sha256).toBe(hash);
    const payload = Buffer.from(
      `1.2.0\nstable\nhttps://mods.example.test/api/v1/updates/packages/${hash}.zip\n${hash}\nNotes ä\nsecond line`
    );
    expect(
      verify(
        "RSA-SHA256",
        payload,
        { key: keys.publicKey, padding: constants.RSA_PKCS1_PADDING },
        Buffer.from(manifest.signature, "base64")
      )
    ).toBe(true);
    await expect(promisify(execFile)(process.execPath, args)).rejects.toThrow();
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});
