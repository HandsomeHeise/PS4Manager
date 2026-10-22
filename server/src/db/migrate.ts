import { readdir, readFile } from "node:fs/promises";
import { join } from "node:path";
import type { Pool, RowDataPacket } from "mysql2/promise";

// Migration files are trusted repository SQL: one statement per semicolon,
// with no stored routines or semicolons in literals. Application connections
// keep multipleStatements disabled.
export async function runMigrations(pool: Pool, directory: string): Promise<void> {
  const connection = await pool.getConnection();
  let locked = false;
  try {
    const [lock] = await connection.query<RowDataPacket[]>(
      "SELECT GET_LOCK('ps4manager-migrations', 30) AS acquired"
    );
    if (lock[0]?.acquired !== 1) throw new Error("Could not acquire migration lock");
    locked = true;
    await connection.query(`CREATE TABLE IF NOT EXISTS schema_migrations (
      name VARCHAR(255) PRIMARY KEY,
      applied_utc DATETIME(6) NOT NULL
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci`);
    const [rows] = await connection.query<RowDataPacket[]>("SELECT name FROM schema_migrations");
    const applied = new Set(rows.map((row) => String(row.name)));
    const files = (await readdir(directory)).filter((name) => name.endsWith(".sql")).sort();
    for (const name of files) {
      if (applied.has(name)) continue;
      const sql = await readFile(join(directory, name), "utf8");
      for (const statement of sql
        .split(";")
        .map((value) => value.trim())
        .filter(Boolean)) {
        await connection.query(statement);
      }
      await connection.execute(
        "INSERT INTO schema_migrations (name, applied_utc) VALUES (?, UTC_TIMESTAMP(6))",
        [name]
      );
    }
  } finally {
    try {
      if (locked) await connection.query("SELECT RELEASE_LOCK('ps4manager-migrations')");
    } catch {
      connection.destroy();
    } finally {
      connection.release();
    }
  }
}
