import { randomBytes } from "node:crypto";
import { mkdtemp, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { GenericContainer, Wait } from "testcontainers";
import type { Pool } from "mysql2/promise";
import { createPool } from "../../src/db/pool.js";

export async function startMysql() {
  const directory = await mkdtemp(join(tmpdir(), "ps4manager-mysql-"));
  const passwordFile = join(directory, "password");
  const password = randomBytes(24).toString("hex");
  await writeFile(passwordFile, password);
  const container = await new GenericContainer("mysql:8.4")
    .withEnvironment({
      MYSQL_ROOT_PASSWORD: randomBytes(24).toString("hex"),
      MYSQL_DATABASE: "ps4manager",
      MYSQL_USER: "ps4manager",
      MYSQL_PASSWORD: password
    })
    .withExposedPorts(3306)
    .withWaitStrategy(Wait.forLogMessage(/ready for connections.*port: 3306/))
    .withStartupTimeout(180_000)
    .start();
  const config = {
    host: container.getHost(),
    port: container.getMappedPort(3306),
    database: "ps4manager",
    user: "ps4manager",
    passwordFile
  };
  const pools = new Set<Pool>();
  function newPool() {
    const pool = createPool(config);
    pools.add(pool);
    return pool;
  }
  return {
    pool: newPool(),
    newPool,
    config,
    async closePool(pool: Pool) {
      pools.delete(pool);
      await pool.end();
    },
    async stop() {
      try {
        await Promise.all([...pools].map((pool) => pool.end()));
      } finally {
        await container.stop();
        await rm(directory, { recursive: true, force: true });
      }
    }
  };
}
