import { expect, it } from "vitest";
import { SecretBox } from "../../src/auth/secret-box.js";
it("authenticates ciphertext with a fresh nonce on each encryption", () => {
  const box = new SecretBox(Buffer.alloc(32, 1)),
    message = Buffer.from("totp test secret");
  const a = box.seal(message),
    b = box.seal(message);
  expect(a.equals(b)).toBe(false);
  expect(box.open(a)).toEqual(message);
  a[a.length - 1] ^= 1;
  expect(() => box.open(a)).toThrow();
  expect(() => new SecretBox(Buffer.alloc(32, 2)).open(b)).toThrow();
});
it("rejects invalid key and truncated ciphertext", () => {
  expect(() => new SecretBox(Buffer.alloc(16))).toThrow();
  expect(() => new SecretBox(Buffer.alloc(32)).open(Buffer.alloc(3))).toThrow();
});
