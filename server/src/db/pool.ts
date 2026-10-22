import { readFileSync } from "node:fs";
import mysql from "mysql2";
import type { Pool } from "mysql2/promise";
import type { DatabaseConfig } from "../config.js";

export function createPool(config: DatabaseConfig): Pool {
  const pool = mysql.createPool({
    host: config.host,
    port: config.port,
    database: config.database,
    user: config.user,
    password: readFileSync(config.passwordFile, "utf8").trimEnd(),
    charset: "utf8mb4_0900_ai_ci",
    timezone: "Z",
    connectionLimit: 10,
    waitForConnections: true,
    queueLimit: 100,
    multipleStatements: false
  });
  pool.on("connection", (connection) => {
    connection.query("SET time_zone = '+00:00'", (error) => {
      if (error) connection.destroy();
    });
  });
  return pool.promise();
}
