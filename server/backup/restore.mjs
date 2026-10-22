import { mkdtemp, readFile, rm, cp, readdir } from "node:fs/promises";
import { createReadStream } from "node:fs";
import { join } from "node:path";
import { tmpdir } from "node:os";
import { spawn } from "node:child_process";
import { pipeline } from "node:stream/promises";
import { verifyArchive, files, exec } from "./archive.mjs";
import { createPool } from "mysql2/promise";
import { runMigrations } from "../dist/db/migrate.js";
const [archive, flag] = process.argv.slice(2);
if (
  process.env.RESTORE_WRITERS_STOPPED !== "1" ||
  !archive ||
  !/^ps4manager-[a-zA-Z0-9-]+\.tar\.gz$/u.test(archive) ||
  (flag && flag !== "--replace-empty-volumes")
)
  throw new Error("Use restore.sh with a backup basename, after stopping writers");
const storage = process.env.STORAGE_ROOT || "/var/lib/ps4manager",
  temporary = await mkdtemp(join(tmpdir(), "ps4manager-restore-"));
if (storage !== "/var/lib/ps4manager") throw new Error("Unexpected restore storage root");
const password = (await readFile(process.env.MYSQL_PASSWORD_FILE, "utf8")).trimEnd();
const pool = createPool({
  host: process.env.MYSQL_HOST,
  user: process.env.MYSQL_USER,
  password,
  database: process.env.MYSQL_DATABASE,
  multipleStatements: false
});
try {
  await verifyArchive(join(process.env.BACKUP_ROOT || "/backups", archive), temporary);
  const [tables] = await pool.query(
    "SELECT TABLE_NAME AS name FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()"
  );
  const entries = await readdir(storage);
  if ((tables.length || entries.length) && flag !== "--replace-empty-volumes")
    throw new Error("Destination is not empty; explicit empty-volume confirmation required");
  if ((await files(storage)).length) throw new Error("Refusing to overwrite artifact files");
  for (const table of tables) {
    if (!/^[a-z_]+$/u.test(table.name)) throw new Error("Unexpected destination table");
    const [rows] = await pool.query(`SELECT COUNT(*) AS count FROM \`${table.name}\``);
    if (Number(rows[0].count) > 0) throw new Error("Refusing to overwrite database rows");
  }
  const child = spawn(
    "mysql",
    [
      "--host=" + process.env.MYSQL_HOST,
      "--user=" + process.env.MYSQL_USER,
      process.env.MYSQL_DATABASE
    ],
    { env: { ...process.env, MYSQL_PWD: password }, stdio: ["pipe", "ignore", "pipe"] }
  );
  child.stderr.resume();
  const completed = new Promise((resolve, reject) => {
    child.once("error", reject);
    child.once("close", (code) =>
      code === 0 ? resolve() : reject(new Error("Database restore failed"))
    );
  });
  await Promise.all([
    pipeline(createReadStream(join(temporary, "database.sql")), child.stdin),
    completed
  ]);
  await cp(join(temporary, "artifacts"), storage, { recursive: true });
  await exec("chown", ["-R", "1000:1000", storage]);
  await runMigrations(pool, "/app/migrations");
  console.log(
    "Restore verified. Run: docker compose up -d; curl --fail http://127.0.0.1:8080/health/ready"
  );
} finally {
  await pool.end();
  await rm(temporary, { recursive: true, force: true });
}
