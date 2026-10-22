import { createHash, randomBytes, randomUUID } from "node:crypto";
import type { Pool, RowDataPacket, ResultSetHeader } from "mysql2/promise";
import { withTransaction } from "../db/transaction.js";
import { HttpError } from "../shared/http-errors.js";
export interface AdminIdentity {
  id: string;
  username: string;
}
export interface AdminSession {
  actor: AdminIdentity;
  csrfToken: string;
  tokenHash: string;
}
export interface AdminUser extends RowDataPacket {
  id: string;
  username: string;
  password_hash: string;
  totp_secret_encrypted: Buffer;
}
export function tokenHash(token: string): string {
  return createHash("sha256").update(token).digest("hex");
}
export class SessionRepository {
  constructor(private readonly pool: Pool) {}
  async user(username: string): Promise<AdminUser | null> {
    const [rows] = await this.pool.query<AdminUser[]>(
      "SELECT id,username,password_hash,totp_secret_encrypted FROM admin_users WHERE username=? AND state='active'",
      [username]
    );
    return rows[0] ?? null;
  }
  async get(token: string, now: Date): Promise<AdminSession | null> {
    const hash = tokenHash(token);
    const [rows] = await this.pool.query<
      (RowDataPacket & { id: string; username: string; csrf_token: string })[]
    >(
      `SELECT u.id,u.username,s.csrf_token FROM admin_sessions s JOIN admin_users u ON u.id=s.admin_user_id
       WHERE s.token_hash=? AND s.expires_utc>? AND u.state='active'`,
      [hash, now]
    );
    const row = rows[0];
    return row
      ? {
          actor: { id: row.id, username: row.username },
          csrfToken: row.csrf_token,
          tokenHash: hash
        }
      : null;
  }
  async create(user: AdminIdentity, now: Date, expiry: Date, totpStep: number, oldToken?: string) {
    const token = randomBytes(32).toString("hex"),
      csrfToken = randomBytes(32).toString("hex");
    await withTransaction(this.pool, async (connection) => {
      const [consumed] = await connection.execute<ResultSetHeader>(
        "UPDATE admin_users SET last_totp_step=? WHERE id=? AND state='active' AND (last_totp_step IS NULL OR last_totp_step<?)",
        [totpStep, user.id, totpStep]
      );
      if (consumed.affectedRows !== 1) throw new HttpError(401, "invalid_credentials");
      if (oldToken)
        await connection.execute("DELETE FROM admin_sessions WHERE token_hash=?", [
          tokenHash(oldToken)
        ]);
      await connection.execute(
        "INSERT INTO admin_sessions (token_hash,admin_user_id,csrf_token,created_utc,expires_utc) VALUES (?,?,?,?,?)",
        [tokenHash(token), user.id, csrfToken, now, expiry]
      );
      await connection.execute(
        "INSERT INTO audit_entries (id,admin_user_id,action,subject,details,created_utc) VALUES (?,?,'admin.login',?,'{}',?)",
        [randomUUID(), user.id, user.id, now]
      );
    });
    return { token, csrfToken };
  }
  async remove(hash: string): Promise<void> {
    await this.pool.execute("DELETE FROM admin_sessions WHERE token_hash=?", [hash]);
  }
}
