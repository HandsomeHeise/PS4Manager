import { afterEach, expect, it } from "vitest";
import { mkdtemp, writeFile, rm, readdir } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { buildZip, type TestEntry } from "../fixtures/build-zips.js";
import { validatePackageZip } from "../../src/storage/zip-validator.js";
const limits = {
  maxPackageBytes: 2_000_000,
  maxExpandedBytes: 4_000_000,
  maxEntries: 100,
  maxCompressionRatio: 100
};
it("rejects a regular file used as an explicit directory ancestor", async () => {
  const path = await fixture([
    manifest(["payload/a"]),
    { name: "payload/a" },
    { name: "payload/a/child/", mode: 0o040755, content: "" }
  ]);
  await expect(validatePackageZip(path, limits)).rejects.toThrow();
});
const directories: string[] = [];
afterEach(async () => {
  await Promise.all(
    directories.splice(0).map((path) => rm(path, { recursive: true, force: true }))
  );
});
async function fixture(entries: TestEntry[]) {
  const directory = await mkdtemp(join(tmpdir(), "ps4manager-zip-"));
  directories.push(directory);
  const path = join(directory, "test.zip");
  await writeFile(path, buildZip(entries));
  return path;
}
function manifest(names: string[]) {
  return {
    name: "manifest.json",
    content: JSON.stringify({
      formatVersion: 1,
      packageType: "files",
      operations: names.map((source, i) => ({ source, target: `/data/test/${i}.lua` }))
    })
  };
}
it("validates payload bytes without extracting", async () => {
  const path = await fixture([manifest(["payload/a.lua"]), { name: "payload/a.lua" }]);
  const result = await validatePackageZip(path, limits);
  expect(result.manifest.operations[0].source).toBe("payload/a.lua");
  expect(result.entries.map((entry) => entry.name)).toEqual(["payload/a.lua"]);
  expect(await readdir(directories[0])).toEqual(["test.zip"]);
});
it.each([
  "../escape.lua",
  "/absolute.lua",
  "payload\\windows.lua",
  "payload/./dot.lua",
  "payload/a/../escape.lua",
  "payload//x",
  "payload/CON",
  "payload/COM¹.txt",
  "payload/lPt².lua",
  "payload/COM³/child.lua",
  "payload/LPT¹",
  "payload/COM²",
  "payload/LPT³.txt",
  `payload/${"a".repeat(256)}`,
  `payload/${"é".repeat(128)}`,
  "payload/a."
])("rejects hostile entry %j", async (name) => {
  const path = await fixture([manifest([name]), { name }]);
  await expect(validatePackageZip(path, limits)).rejects.toThrow();
});
it.each([
  [{ name: "payload/Foo.lua" }, { name: "payload/foo.lua" }],
  [{ name: "payload/link.lua", mode: 0o120777 }],
  [{ name: "payload/encrypted.lua", encrypted: true }],
  [{ name: "payload/bomb.lua", content: "0".repeat(100_000), deflate: true }],
  [{ name: "payload/corrupt.lua", badCrc: true }],
  [{ name: "payload/file" }, { name: "payload/file/child" }]
])("rejects unsafe archive contents", async (...entries) => {
  const path = await fixture([manifest(entries.map((entry) => entry.name)), ...entries]);
  await expect(validatePackageZip(path, limits)).rejects.toThrow();
});
it("rejects undeclared payload and missing manifest", async () => {
  const entry = { name: "payload/extra.lua" };
  await expect(
    validatePackageZip(await fixture([manifest(["payload/a"]), entry]), limits)
  ).rejects.toThrow();
  await expect(validatePackageZip(await fixture([entry]), limits)).rejects.toThrow();
});
it.each([{ maxEntries: 1 }, { maxExpandedBytes: 10 }, { maxPackageBytes: 10 }])(
  "enforces archive limits",
  async (overrides) => {
    const path = await fixture([manifest(["payload/a"]), { name: "payload/a" }]);
    await expect(validatePackageZip(path, { ...limits, ...overrides })).rejects.toThrow();
  }
);
it("caps manifest bytes independently of the archive limit", async () => {
  const path = await fixture([{ name: "manifest.json", content: " ".repeat(1_048_577) }]);
  await expect(validatePackageZip(path, limits)).rejects.toThrow();
});
