import { stat } from "node:fs/promises";
import { crc32 } from "node:zlib";
import { openPromise } from "yauzl";
import { parseManifest, validateRelativePath, type PackageManifest } from "../domain/manifest.js";
export interface ZipLimits {
  maxPackageBytes: number;
  maxExpandedBytes: number;
  maxEntries: number;
  maxCompressionRatio: number;
}
export interface ValidatedPackage {
  manifest: PackageManifest;
  entries: { name: string; size: number }[];
}

export async function validatePackageZip(
  path: string,
  limits: ZipLimits
): Promise<ValidatedPackage> {
  if (Object.values(limits).some((value) => !Number.isSafeInteger(value) || value <= 0))
    throw new Error("Invalid ZIP limits");
  if ((await stat(path)).size > limits.maxPackageBytes)
    throw new Error("Package size limit exceeded");
  const zip = await openPromise(path, {
    lazyEntries: true,
    autoClose: true,
    strictFileNames: true,
    validateEntrySizes: true
  });
  const entries: ValidatedPackage["entries"] = [],
    names = new Set<string>();
  let manifest: PackageManifest | undefined;
  let count = 0,
    expanded = 0,
    compressed = 0;
  try {
    for await (const entry of zip.eachEntry()) {
      if (++count > limits.maxEntries) throw new Error("ZIP entry limit exceeded");
      const name = entry.fileName;
      const directory = name.endsWith("/");
      validateRelativePath(directory ? name.slice(0, -1) : name);
      const folded = name.replace(/\/$/u, "").toLowerCase();
      if (names.has(folded)) throw new Error("Case-colliding ZIP names");
      names.add(folded);
      const mode = (entry.externalFileAttributes >>> 16) & 0o170000;
      if (
        (mode !== 0 && mode !== 0o100000 && mode !== 0o040000) ||
        (directory && mode === 0o100000) ||
        (!directory && mode === 0o040000) ||
        (entry.generalPurposeBitFlag & 1) !== 0
      )
        throw new Error("Unsupported ZIP entry type");
      expanded += entry.uncompressedSize;
      compressed += entry.compressedSize;
      if (
        !Number.isSafeInteger(expanded) ||
        !Number.isSafeInteger(compressed) ||
        expanded > limits.maxExpandedBytes ||
        compressed > limits.maxPackageBytes ||
        entry.uncompressedSize > Math.max(1, entry.compressedSize) * limits.maxCompressionRatio
      )
        throw new Error("ZIP expansion limit exceeded");
      if (directory) {
        if (name !== "payload/" && !name.startsWith("payload/"))
          throw new Error("Unexpected directory");
        if (entry.uncompressedSize !== 0) throw new Error("Directory contains data");
        continue;
      }
      if (name !== "manifest.json" && !name.startsWith("payload/"))
        throw new Error("Undeclared ZIP file");
      if (name === "manifest.json" && entry.uncompressedSize > 1_048_576)
        throw new Error("Manifest exceeds 1 MiB");
      const stream = await zip.openReadStreamPromise(entry);
      const chunks: Buffer[] = [];
      let bytes = 0,
        checksum = 0;
      for await (const chunk of stream as AsyncIterable<Buffer>) {
        bytes += chunk.length;
        if (bytes > entry.uncompressedSize) {
          stream.destroy();
          throw new Error("ZIP size mismatch");
        }
        checksum = crc32(chunk, checksum);
        if (name === "manifest.json") chunks.push(chunk);
      }
      if (bytes !== entry.uncompressedSize || checksum !== entry.crc32)
        throw new Error("ZIP integrity mismatch");
      if (name === "manifest.json")
        manifest = parseManifest(
          new TextDecoder("utf-8", { fatal: true }).decode(Buffer.concat(chunks))
        );
      else entries.push({ name, size: bytes });
    }
    if (!manifest) throw new Error("Missing manifest.json");
    const files = new Set(entries.map((entry) => entry.name.toLowerCase()));
    for (const name of names) {
      const parts = name.split("/");
      for (let i = 1; i < parts.length; i++) {
        if (files.has(parts.slice(0, i).join("/"))) throw new Error("File/directory ZIP conflict");
      }
    }
    const declared = new Set(manifest.operations.map((op) => op.source));
    if (entries.length !== declared.size || entries.some((entry) => !declared.has(entry.name)))
      throw new Error("Payload does not match manifest");
    return { manifest, entries };
  } finally {
    zip.close();
  }
}
