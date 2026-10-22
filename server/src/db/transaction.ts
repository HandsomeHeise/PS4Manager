import type { Pool, PoolConnection } from "mysql2/promise";

export async function withTransaction<T>(
  pool: Pool,
  work: (connection: PoolConnection) => Promise<T>
): Promise<T> {
  const connection = await pool.getConnection();
  try {
    return await inTransaction(connection, work);
  } finally {
    connection.release();
  }
}

export async function inTransaction<T>(
  connection: PoolConnection,
  work: (connection: PoolConnection) => Promise<T>
): Promise<T> {
  await connection.beginTransaction();
  try {
    const result = await work(connection);
    await connection.commit();
    return result;
  } catch (error) {
    try {
      await connection.rollback();
    } catch {
      connection.destroy();
    }
    throw error;
  }
}
