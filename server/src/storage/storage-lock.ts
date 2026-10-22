import type { Pool, PoolConnection, RowDataPacket } from "mysql2/promise";
// Serializes filesystem publication with garbage collection across processes.
// Acquire this before any row locks, and retain it until the transaction commits.
export async function withStorageLock<T>(
  pool: Pool,
  work: (connection: PoolConnection) => Promise<T>
): Promise<T> {
  const connection = await pool.getConnection();
  let acquired = false;
  try {
    const [rows] = await connection.query<RowDataPacket[]>(
      "SELECT GET_LOCK('ps4manager-artifacts', 30) AS acquired"
    );
    if (rows[0]?.acquired !== 1) throw new Error("Artifact storage busy");
    acquired = true;
    return await work(connection);
  } finally {
    try {
      if (acquired) await connection.query("SELECT RELEASE_LOCK('ps4manager-artifacts')");
    } catch {
      connection.destroy();
    } finally {
      connection.release();
    }
  }
}
