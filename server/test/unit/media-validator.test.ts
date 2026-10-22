import { afterEach, expect, it } from "vitest";
import { mkdtemp, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { validatePreview } from "../../src/storage/media-validator.js";
const dirs: string[] = [];
afterEach(async () => {
  await Promise.all(dirs.splice(0).map((path) => rm(path, { recursive: true, force: true })));
});
async function fixture(bytes: Buffer) {
  const dir = await mkdtemp(join(tmpdir(), "ps4manager-media-"));
  dirs.push(dir);
  const path = join(dir, "preview.bin");
  await writeFile(path, bytes);
  return path;
}
it.each([
  ["image/png", "89504e470d0a1a0a0000000d49484452"],
  ["image/jpeg", "ffd8ffe000104a46494600010100000100010000ffd9"],
  ["video/mp4", "000000186674797069736f6d0000020069736f6d6d703432"]
])("accepts matching %s signature and rejects mismatches", async (type, hex) => {
  const path = await fixture(Buffer.from(hex, "hex"));
  expect((await validatePreview(path, type, 100)).contentType).toBe(type);
  await expect(validatePreview(path, "text/html", 100)).rejects.toThrow();
  await expect(validatePreview(path, type, 2)).rejects.toThrow();
});
it("does not authorize media from its extension or declaration", async () => {
  await expect(
    validatePreview(await fixture(Buffer.from("<script>alert(1)</script>")), "image/png", 100)
  ).rejects.toThrow();
});
