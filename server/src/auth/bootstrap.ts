import { randomBytes, randomUUID } from "node:crypto";
import { readFile } from "node:fs/promises";
import { createInterface } from "node:readline/promises";
import { Writable } from "node:stream";
import { loadConfig } from "../config.js";
import { createPool } from "../db/pool.js";
import { hashPassword } from "./password.js";
import { SecretBox } from "./secret-box.js";
export function enrollmentUri(username: string, secret: Buffer): string {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  let buffer = 0,
    bits = 0,
    encoded = "";
  for (const byte of secret) {
    buffer = (buffer << 8) | byte;
    bits += 8;
    while (bits >= 5) {
      bits -= 5;
      encoded += alphabet[(buffer >>> bits) & 31];
    }
  }
  if (bits > 0) encoded += alphabet[(buffer << (5 - bits)) & 31];
  return `otpauth://totp/${encodeURIComponent("PS4Manager:" + username)}?secret=${encoded}&issuer=PS4Manager&algorithm=SHA1&digits=6&period=30`;
}
export async function createAdminCli(): Promise<void> {
  if (!process.stdin.isTTY || !process.stdout.isTTY)
    throw new Error("Admin creation requires an interactive terminal");
  const config = loadConfig(process.env),
    box = new SecretBox(await readFile(config.totpEncryptionKeyFile));
  let muted = false;
  const output = new Writable({
    write(chunk: Buffer, _encoding, done) {
      if (!muted) process.stdout.write(chunk);
      done();
    }
  });
  const prompt = createInterface({ input: process.stdin, output, terminal: true });
  let username: string, password: string;
  try {
    username = (await prompt.question("Administrator username: ")).trim();
    if (!/^[A-Za-z0-9_.-]{1,100}$/u.test(username)) throw new Error("Invalid username");
    process.stdout.write("Password (hidden): ");
    muted = true;
    password = await prompt.question("");
  } finally {
    prompt.close();
    process.stdout.write("\n");
  }
  const hash = await hashPassword(password),
    secret = randomBytes(20),
    pool = createPool(config.database);
  try {
    // Unique username constraint intentionally rejects overwrites.
    await pool.execute(
      "INSERT INTO admin_users (id,username,password_hash,totp_secret_encrypted) VALUES (?,?,?,?)",
      [randomUUID(), username, hash, box.seal(secret)]
    );
    process.stdout.write(enrollmentUri(username, secret) + "\n");
  } finally {
    secret.fill(0);
    await pool.end();
  }
}
