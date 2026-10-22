import { createHmac, timingSafeEqual } from "node:crypto";
export function verifyTotp(secret: Buffer, token: string, now: Date, window: number): boolean {
  return matchTotpStep(secret, token, now, window) !== null;
}
export function matchTotpStep(
  secret: Buffer,
  token: string,
  now: Date,
  window: number
): number | null {
  if (
    !/^[0-9]{6}$/u.test(token) ||
    !Number.isInteger(window) ||
    window < 0 ||
    window > 2 ||
    !Number.isFinite(now.getTime()) ||
    now.getTime() < 0
  )
    return null;
  const current = Math.floor(now.getTime() / 30_000);
  let accepted: number | null = null;
  for (let step = Math.max(0, current - window); step <= current + window; step++) {
    const counter = Buffer.alloc(8);
    counter.writeBigUInt64BE(BigInt(step));
    const mac = createHmac("sha1", secret).update(counter).digest(),
      offset = mac[mac.length - 1]! & 15;
    const expected = String((mac.readUInt32BE(offset) & 0x7fffffff) % 1_000_000).padStart(6, "0");
    if (timingSafeEqual(Buffer.from(expected), Buffer.from(token))) accepted = step;
  }
  return accepted;
}
