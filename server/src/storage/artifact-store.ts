import { createHash, randomUUID } from "node:crypto";
import { createReadStream } from "node:fs";
import { mkdir, open, link, unlink, lstat, chmod } from "node:fs/promises";
import { join, dirname, resolve, extname } from "node:path";
import type { Readable } from "node:stream";
export interface StagedArtifact {
  path: string;
  sha256: string;
  size: number;
  extension: string;
}
export interface StoredArtifact {
  storageKey: string;
  sha256: string;
  size: number;
}
async function hashFile(path: string): Promise<{ sha256: string; size: number }> {
  if (!(await lstat(path)).isFile()) throw new Error("Artifact must be a regular file");
  const hash = createHash("sha256");
  let size = 0;
  for await (const chunk of createReadStream(path) as AsyncIterable<Buffer>) {
    hash.update(chunk);
    size += chunk.length;
  }
  return { sha256: hash.digest("hex"), size };
}
export class ArtifactStore {
  readonly root: string;
  constructor(root: string) {
    this.root = resolve(root);
  }
  stagedPath(path: string): string {
    const absolute = resolve(path);
    if (dirname(absolute) !== join(this.root, "staging")) throw new Error("Invalid staging path");
    return absolute;
  }
  async inspectStaged(path: string): Promise<StagedArtifact> {
    path = this.stagedPath(path);
    return { path, ...(await hashFile(path)), extension: "zip" };
  }
  artifactPath(storageKey: string): string {
    if (
      !/^(?:packages\/(?:mods|updates)|previews)\/[a-f0-9]{2}\/[a-f0-9]{64}\.(?:zip|png|jpg|mp4)$/u.test(
        storageKey
      )
    )
      throw new Error("Invalid artifact key");
    return join(this.root, storageKey);
  }
  async inspect(storageKey: string): Promise<StoredArtifact> {
    return { storageKey, ...(await hashFile(this.artifactPath(storageKey))) };
  }
  async stage(stream: Readable, originalName: string, maxBytes: number): Promise<StagedArtifact> {
    if (!Number.isSafeInteger(maxBytes) || maxBytes <= 0) throw new Error("Invalid staging limit");
    const extension = extname(originalName).slice(1).toLowerCase();
    if (!["zip", "png", "jpg", "jpeg", "mp4"].includes(extension))
      throw new Error("Unsupported artifact extension");
    const directory = join(this.root, "staging");
    await mkdir(this.root, { recursive: true, mode: 0o755 });
    await mkdir(directory, { recursive: true, mode: 0o700 });
    await chmod(directory, 0o700);
    const path = join(directory, randomUUID()),
      file = await open(path, "wx", 0o600);
    const hash = createHash("sha256");
    let size = 0;
    try {
      for await (const chunk of stream as AsyncIterable<Buffer | string>) {
        const bytes = typeof chunk === "string" ? Buffer.from(chunk) : chunk;
        size += bytes.length;
        if (!Number.isSafeInteger(size) || size > maxBytes)
          throw new Error("Artifact size limit exceeded");
        hash.update(bytes);
        await file.writeFile(bytes);
      }
      if (size === 0) throw new Error("Empty artifact");
      await file.sync();
      await file.close();
      return {
        path,
        sha256: hash.digest("hex"),
        size,
        extension: extension === "jpeg" ? "jpg" : extension
      };
    } catch (error) {
      stream.destroy();
      await file.close();
      await unlink(path);
      throw error;
    }
  }
  async publish(
    staged: StagedArtifact,
    kind: "mods" | "updates" | "previews"
  ): Promise<StoredArtifact> {
    if (
      dirname(resolve(staged.path)) !== join(this.root, "staging") ||
      !/^[a-f0-9]{64}$/u.test(staged.sha256) ||
      !["mods", "updates", "previews"].includes(kind) ||
      !(kind === "previews" ? ["png", "jpg", "mp4"] : ["zip"]).includes(staged.extension)
    )
      throw new Error("Invalid staged artifact");
    const actual = await hashFile(staged.path);
    if (actual.sha256 !== staged.sha256 || actual.size !== staged.size)
      throw new Error("Staged artifact changed");
    const base = kind === "previews" ? "previews" : `packages/${kind}`;
    const storageKey = `${base}/${staged.sha256.slice(0, 2)}/${staged.sha256}.${staged.extension}`;
    const target = join(this.root, storageKey);
    await mkdir(dirname(target), { recursive: true });
    // Staging's private directory prevents access until the final hard link
    // makes this inode available to Nginx under its own unprivileged UID.
    await chmod(staged.path, 0o644);
    try {
      // POSIX rename overwrites existing files. Hard-link publication is atomic
      // and refuses replacement even across concurrent publisher processes.
      await link(staged.path, target);
    } catch (error) {
      if (!(error instanceof Error) || !("code" in error) || error.code !== "EEXIST") throw error;
      const existing = await hashFile(target);
      if (existing.sha256 !== staged.sha256 || existing.size !== staged.size)
        throw new Error("Artifact destination integrity mismatch", { cause: error });
    }
    await unlink(staged.path);
    return { storageKey, sha256: staged.sha256, size: staged.size };
  }
}
