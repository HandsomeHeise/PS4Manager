import { fileURLToPath } from "node:url";
import { loadConfig } from "./config.js";
import { createPool } from "./db/pool.js";
import { runMigrations } from "./db/migrate.js";
const pool = createPool(loadConfig(process.env).database);
try {
  await runMigrations(pool, fileURLToPath(new URL("../migrations/", import.meta.url)));
} finally {
  await pool.end();
}
