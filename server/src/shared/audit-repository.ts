import { randomUUID } from "node:crypto";
import type { PoolConnection } from "mysql2/promise";
import type { AdminIdentity } from "../auth/session-repository.js";
export async function appendAudit(
  connection: PoolConnection,
  actor: AdminIdentity,
  action: string,
  subject: string,
  now: Date,
  details: Record<string, unknown> = {}
): Promise<void> {
  await connection.execute(
    "INSERT INTO audit_entries (id,admin_user_id,action,subject,details,created_utc) VALUES (?,?,?,?,?,?)",
    [randomUUID(), actor.id, action, subject, JSON.stringify(details), now]
  );
}
