import { afterAll, beforeAll, describe, expect, it } from "vitest";
import { mkdtemp, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import type { RowDataPacket } from "mysql2/promise";
import { startMysql } from "../helpers/mysql.js";
import { runMigrations } from "../../src/db/migrate.js";
import { withTransaction } from "../../src/db/transaction.js";
import { buildApp } from "../../src/app.js";
import { loadConfig } from "../../src/config.js";
import { SystemClock } from "../../src/clock.js";

describe("MySQL persistence boundary", () => {
  let db: Awaited<ReturnType<typeof startMysql>>;
  const migrations = fileURLToPath(new URL("../../migrations/", import.meta.url));
  beforeAll(async () => {
    db = await startMysql();
  }, 240_000);
  afterAll(async () => {
    if (db) await db.stop();
  }, 60_000);

  it("creates the complete version-one schema exactly once under concurrent runners", async () => {
    await Promise.all([runMigrations(db.pool, migrations), runMigrations(db.pool, migrations)]);
    await runMigrations(db.pool, migrations);
    const [rows] = await db.pool.query<RowDataPacket[]>(
      "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE()"
    );
    expect(rows.map((row) => row.TABLE_NAME).sort()).toEqual([
      "admin_sessions",
      "admin_users",
      "app_releases",
      "artifacts",
      "audit_entries",
      "mod_media",
      "mod_releases",
      "mods",
      "release_compatibility",
      "schema_migrations"
    ]);
    const [applied] = await db.pool.query<RowDataPacket[]>("SELECT name FROM schema_migrations");
    expect(applied.map((row) => row.name).sort()).toEqual([
      "001-initial.sql",
      "002-admin-totp-step.sql"
    ]);
    // Recover safely if DDL committed but the migration marker did not.
    await db.pool.execute("DELETE FROM schema_migrations WHERE name='002-admin-totp-step.sql'");
    await runMigrations(db.pool, migrations);
  });

  it("commits successful work and rolls back failed work", async () => {
    const insert =
      "INSERT INTO mods (id, slug, title, author, summary, description, category, state) VALUES (?, ?, 'Title', 'Author', 'Summary', 'Description', 'misc', 'draft')";
    const result = await withTransaction(db.pool, async (connection) => {
      await connection.execute(insert, ["11111111-1111-4111-8111-111111111111", "committed"]);
      return "committed";
    });
    expect(result).toBe("committed");
    await expect(
      withTransaction(db.pool, async (connection) => {
        await connection.execute(insert, ["22222222-2222-4222-8222-222222222222", "rollback"]);
        throw new Error("inject rollback");
      })
    ).rejects.toThrow("inject rollback");
    const [rows] = await db.pool.query<RowDataPacket[]>("SELECT slug FROM mods ORDER BY slug");
    expect(rows.map((row) => row.slug)).toEqual(["committed"]);
    await expect(
      db.pool.execute(insert, ["33333333-3333-4333-8333-333333333333", "committed"])
    ).rejects.toThrow();
  });

  it("does not record failed migration and releases its advisory lock for retry", async () => {
    const directory = await mkdtemp(join(tmpdir(), "ps4manager-migration-"));
    const secondPool = db.newPool();
    try {
      await writeFile(join(directory, "002-probe.sql"), "INVALID SQL;");
      await expect(runMigrations(db.pool, directory)).rejects.toThrow();
      const [rows] = await db.pool.query<RowDataPacket[]>(
        "SELECT name FROM schema_migrations WHERE name = '002-probe.sql'"
      );
      expect(rows).toHaveLength(0);
      await writeFile(join(directory, "002-probe.sql"), "SELECT 1;");
      await runMigrations(secondPool, directory);
      const [locks] = await secondPool.query<RowDataPacket[]>(
        "SELECT IS_FREE_LOCK('ps4manager-migrations') AS free"
      );
      expect(locks[0].free).toBe(1);
    } finally {
      await rm(directory, { recursive: true, force: true });
    }
  });

  it("uses UTC database sessions", async () => {
    const [rows] = await db.pool.query<RowDataPacket[]>("SELECT @@session.time_zone AS zone");
    expect(rows[0].zone).toBe("+00:00");
  });

  it("reports readiness only while the database can answer queries", async () => {
    const pool = db.newPool();
    const config = loadConfig({
      NODE_ENV: "test",
      HOST: "127.0.0.1",
      PORT: "3100",
      PUBLIC_BASE_URL: "https://mods.example.test",
      MYSQL_HOST: db.config.host,
      MYSQL_PORT: String(db.config.port),
      MYSQL_DATABASE: db.config.database,
      MYSQL_USER: db.config.user,
      MYSQL_PASSWORD_FILE: db.config.passwordFile,
      STORAGE_ROOT: "/tmp/storage",
      SESSION_SECRET_FILE: "/tmp/session",
      TOTP_ENCRYPTION_KEY_FILE: "/tmp/totp",
      MOD_PRIVATE_KEY_FILE: "/tmp/mod",
      UPDATE_PUBLIC_KEY_FILE: "/tmp/update"
    });
    const app = await buildApp({ config, clock: new SystemClock(), pool });
    try {
      const ready = await app.inject("/health/ready");
      expect(ready.statusCode).toBe(200);
      expect(ready.json()).toEqual({ status: "ready" });
      await db.closePool(pool);
      const down = await app.inject("/health/ready");
      expect(down.statusCode).toBe(503);
      expect(down.json()).toEqual({ status: "unavailable" });
      expect((await app.inject("/health/live")).statusCode).toBe(200);
    } finally {
      await app.close();
    }
  });
});
