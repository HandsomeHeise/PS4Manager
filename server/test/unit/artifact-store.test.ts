import { afterEach, beforeEach, expect, it } from "vitest";
import { mkdtemp, rm, readFile, readdir, mkdir, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join, dirname } from "node:path";
import { Readable } from "node:stream";
import { ArtifactStore } from "../../src/storage/artifact-store.js";
let root: string, store: ArtifactStore;
beforeEach(async () => {
  root = await mkdtemp(join(tmpdir(), "ps4manager-artifacts-"));
  store = new ArtifactStore(root);
});
afterEach(async () => {
  await rm(root, { recursive: true, force: true });
});
const hash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
it("stages hashed bytes and publishes a single immutable artifact even concurrently", async () => {
  const a = await store.stage(Readable.from(["a", "bc"]), "example.zip", 3);
  expect(a.sha256).toBe(hash);
  expect(a.size).toBe(3);
  expect(await readFile(a.path, "utf8")).toBe("abc");
  const b = await store.stage(Readable.from(["abc"]), "other.zip", 3);
  const [first, second] = await Promise.all([store.publish(a, "mods"), store.publish(b, "mods")]);
  expect(first.storageKey).toBe(`packages/mods/ba/${hash}.zip`);
  expect(second).toEqual(first);
  expect(await readFile(join(root, first.storageKey), "utf8")).toBe("abc");
  expect(await readdir(join(root, "staging"))).toEqual([]);
});
it("removes partial uploads after a size violation or stream failure", async () => {
  await expect(store.stage(Readable.from(["abcd"]), "x.zip", 3)).rejects.toThrow();
  async function* broken() {
    yield Buffer.from("a");
    throw new Error("upload failed");
  }
  await expect(store.stage(Readable.from(broken()), "x.zip", 3)).rejects.toThrow("upload failed");
  expect(await readdir(join(root, "staging"))).toEqual([]);
});
it("never overwrites a different file at the content-addressed destination", async () => {
  const staged = await store.stage(Readable.from(["abc"]), "x.zip", 3);
  const target = join(root, `packages/mods/ba/${hash}.zip`);
  await mkdir(dirname(target), { recursive: true });
  await writeFile(target, "wrong");
  await expect(store.publish(staged, "mods")).rejects.toThrow();
  expect(await readFile(target, "utf8")).toBe("wrong");
});
it("rejects changed staging bytes before publication", async () => {
  const staged = await store.stage(Readable.from(["abc"]), "x.zip", 3);
  await writeFile(staged.path, "changed");
  await expect(store.publish(staged, "mods")).rejects.toThrow();
});
