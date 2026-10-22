import { randomBytes, scrypt, timingSafeEqual } from "node:crypto";
function derive(password: string, salt: Buffer): Promise<Buffer> {
  return new Promise((resolve, reject) =>
    scrypt(password, salt, 32, { N: 16384, r: 8, p: 1, maxmem: 64 * 1024 * 1024 }, (error, key) =>
      error ? reject(error) : resolve(key)
    )
  );
}
export async function hashPassword(password: string): Promise<string> {
  if (password.length < 16 || Buffer.byteLength(password) > 1024)
    throw new Error("Password must be 16 characters or longer and at most 1024 bytes");
  const salt = randomBytes(16),
    key = await derive(password, salt);
  return `scrypt$16384$8$1$${salt.toString("base64")}$${key.toString("base64")}`;
}
export async function verifyPassword(password: string, encoded: string): Promise<boolean> {
  if (Buffer.byteLength(password) > 1024) return false;
  const match = /^scrypt\$16384\$8\$1\$([A-Za-z0-9+/]{22}==)\$([A-Za-z0-9+/]{43}=)$/u.exec(encoded);
  if (!match?.[1] || !match[2]) return false;
  const salt = Buffer.from(match[1], "base64"),
    expected = Buffer.from(match[2], "base64");
  return timingSafeEqual(await derive(password, salt), expected);
}
