import { createHash } from "node:crypto";
import { createReadStream } from "node:fs";
import { readdir, readFile, mkdir, lstat } from "node:fs/promises";
import { join } from "node:path";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
export const exec = promisify(execFile);
export async function digest(path) {
  const hash = createHash("sha256");
  for await (const bytes of createReadStream(path)) hash.update(bytes);
  return hash.digest("hex");
}
export async function files(root, prefix = "") {
  const found = [];
  for (const entry of await readdir(join(root, prefix), { withFileTypes: true })) {
    const path = prefix ? `${prefix}/${entry.name}` : entry.name;
    if (entry.isDirectory()) found.push(...(await files(root, path)));
    else if (entry.isFile()) found.push(path);
    else throw new Error("Backup contains non-regular file");
  }
  return found.sort();
}
function allowed(path) {
  return (
    ["database.sql", "inventory.json"].includes(path) ||
    /^artifacts\/(?:packages\/(?:mods|updates)|previews)\/[a-f0-9]{2}\/[a-f0-9]{64}\.(?:zip|png|jpg|mp4)$/u.test(
      path
    )
  );
}
export async function verifyArchive(archive, destination) {
  const { stdout: listing } = await exec("tar", ["-tzf", archive], { maxBuffer: 32 * 1024 * 1024 });
  const names = listing.trim().split(/\r?\n/u);
  for (const name of names) {
    const path = name.replace(/^\.\//u, "");
    if (path.endsWith("/") || !path) {
      if (path.split("/").some((part) => part === "..") || path.startsWith("/"))
        throw new Error("Unsafe archive directory");
    } else if (!allowed(path)) throw new Error("Unsafe archive entry");
  }
  const { stdout: types } = await exec("tar", ["-tvzf", archive], { maxBuffer: 64 * 1024 * 1024 });
  if (
    types
      .trim()
      .split(/\r?\n/u)
      .some((line) => !["-", "d"].includes(line[0]))
  )
    throw new Error("Archive links are forbidden");
  await mkdir(destination, { recursive: true });
  await exec("tar", [
    "-xzf",
    archive,
    "--no-same-owner",
    "--no-same-permissions",
    "-C",
    destination
  ]);
  const inventoryPath = join(destination, "inventory.json");
  if ((await lstat(inventoryPath)).size > 32 * 1024 * 1024) throw new Error("Inventory too large");
  const inventory = JSON.parse(await readFile(inventoryPath, "utf8"));
  const actual = (await files(destination)).filter((path) => path !== "inventory.json");
  if (
    !Array.isArray(inventory) ||
    actual.length !== inventory.length ||
    new Set(inventory.map((i) => i.path)).size !== inventory.length
  )
    throw new Error("Inventory mismatch");
  for (const item of inventory) {
    if (
      typeof item.path !== "string" ||
      !allowed(item.path) ||
      !actual.includes(item.path) ||
      (await digest(join(destination, item.path))) !== item.sha256
    )
      throw new Error("Backup checksum mismatch");
  }
}
