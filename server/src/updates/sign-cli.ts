import { parseArgs } from "node:util";
import { createHash } from "node:crypto";
import { createReadStream } from "node:fs";
import { readFile, writeFile } from "node:fs/promises";
import { RsaSigner } from "../publishing/rsa-signer.js";
import { parseUpdateManifest, signedUpdatePayload } from "./update-manifest.js";
export async function signUpdateCli(args: string[]): Promise<void> {
  const { values } = parseArgs({
    args,
    options: {
      version: { type: "string" },
      channel: { type: "string" },
      url: { type: "string" },
      "notes-file": { type: "string" },
      package: { type: "string" },
      "private-key": { type: "string" },
      out: { type: "string" }
    },
    strict: true,
    allowPositionals: false
  });
  if (
    !values.version ||
    !values.channel ||
    !values.url ||
    !values["notes-file"] ||
    !values.package ||
    !values["private-key"] ||
    !values.out
  )
    throw new Error(
      "Required: --version --channel --url --notes-file --package --private-key --out"
    );
  const hash = createHash("sha256");
  for await (const chunk of createReadStream(values.package)) hash.update(chunk as Buffer);
  const sha256 = hash.digest("hex"),
    manifest = parseUpdateManifest({
      version: values.version,
      channel: values.channel,
      url: values.url.replace("{sha256}", sha256),
      sha256,
      notes: await readFile(values["notes-file"], "utf8"),
      signature: ""
    });
  const signer = new RsaSigner(await readFile(values["private-key"]));
  manifest.signature = (await signer.sign(signedUpdatePayload(manifest))).signatureBase64;
  await writeFile(values.out, JSON.stringify(manifest, null, 2) + "\n", {
    flag: "wx",
    mode: 0o600
  });
}
