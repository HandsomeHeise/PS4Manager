import { createWriteStream } from "node:fs";
import {
  mkdtemp,
  mkdir,
  readFile,
  writeFile,
  cp,
  rename,
  rm,
  readdir,
  stat,
  unlink
} from "node:fs/promises";
import { join } from "node:path";
import { tmpdir } from "node:os";
import { spawn } from "node:child_process";
import { pipeline } from "node:stream/promises";
import { digest, files, verifyArchive, exec } from "./archive.mjs";
import { createPool } from "../dist/db/pool.js";
import { withStorageLock } from "../dist/storage/storage-lock.js";
const root = process.env.BACKUP_ROOT || "/backups",
  storage = process.env.STORAGE_ROOT || "/var/lib/ps4manager",
  remote = process.env.RCLONE_REMOTE;
const days = Number(process.env.BACKUP_RETENTION_DAYS || 14);
if (!remote || !Number.isInteger(days) || days < 1)
  throw new Error("Backup destination and retention required");
await mkdir(root, { recursive: true, mode: 0o700 });
const temporary = await mkdtemp(join(tmpdir(), "ps4manager-backup-"));
const name = `ps4manager-${new Date().toISOString().replace(/[:.]/gu, "-")}.tar.gz`,
  partial = join(root, name + ".partial"),
  final = join(root, name);
const pool = createPool({
  host: process.env.MYSQL_HOST,
  port: Number(process.env.MYSQL_PORT || 3306),
  database: process.env.MYSQL_DATABASE,
  user: process.env.MYSQL_USER,
  passwordFile: process.env.MYSQL_PASSWORD_FILE
});
try {
  const snapshot = join(temporary, "snapshot");
  await mkdir(snapshot);
  await withStorageLock(pool, async () => {
    const password = (await readFile(process.env.MYSQL_PASSWORD_FILE, "utf8")).trimEnd();
    const child = spawn(
      "mysqldump",
      [
        "--host=" + process.env.MYSQL_HOST,
        "--user=" + process.env.MYSQL_USER,
        "--single-transaction",
        "--routines",
        "--events",
        "--no-tablespaces",
        "--set-gtid-purged=OFF",
        process.env.MYSQL_DATABASE
      ],
      { env: { ...process.env, MYSQL_PWD: password }, stdio: ["ignore", "pipe", "pipe"] }
    );
    child.stderr.resume();
    const completed = new Promise((resolve, reject) => {
      child.once("error", reject);
      child.once("close", (code) =>
        code === 0 ? resolve() : reject(new Error("Database backup failed"))
      );
    });
    await Promise.all([
      pipeline(child.stdout, createWriteStream(join(snapshot, "database.sql"), { mode: 0o600 })),
      completed
    ]);
    await mkdir(join(snapshot, "artifacts"));
    for (const dir of ["packages", "previews"]) {
      try {
        await stat(join(storage, dir));
      } catch (error) {
        if (error.code === "ENOENT") continue;
        throw error;
      }
      await cp(join(storage, dir), join(snapshot, "artifacts", dir), {
        recursive: true,
        errorOnExist: true
      });
    }
    const inventory = [];
    for (const path of await files(snapshot))
      inventory.push({ path, sha256: await digest(join(snapshot, path)) });
    await writeFile(join(snapshot, "inventory.json"), JSON.stringify(inventory), { mode: 0o600 });
  });
  await exec("tar", ["-czf", partial, "-C", snapshot, "."]);
  await verifyArchive(partial, join(temporary, "verified"));
  await rename(partial, final);
  await exec("rclone", ["copy", final, remote, "--config", process.env.RCLONE_CONFIG], {
    maxBuffer: 1024 * 1024
  });
  await writeFile(join(root, "latest.ok"), name + "\n", { mode: 0o600 });
  const archives = (await readdir(root)).filter((n) => /^ps4manager-.*\.tar\.gz$/u.test(n)).sort();
  for (const old of archives.slice(0, -1))
    if (Date.now() - (await stat(join(root, old))).mtimeMs > days * 86400000)
      await unlink(join(root, old));
  console.log("Backup verified and copied offsite: " + name);
} finally {
  await pool.end();
  await rm(temporary, { recursive: true, force: true });
  await rm(partial, { force: true });
}
