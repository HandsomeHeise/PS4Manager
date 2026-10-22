import { expect, it } from "vitest";
import { verifyTotp } from "../../src/auth/totp.js";
const key = Buffer.from("12345678901234567890");
it("matches the RFC6238 SHA1 vector at 59 seconds using six digits", () => {
  expect(verifyTotp(key, "287082", new Date(59_000), 0)).toBe(true);
  expect(verifyTotp(key, "287082", new Date(89_000), 1)).toBe(true);
  expect(verifyTotp(key, "287082", new Date(149_000), 1)).toBe(false);
});
it.each(["", "12345", "1234567", "abcdef", "287082\n"])("rejects malformed code %j", (token) =>
  expect(verifyTotp(key, token, new Date(59_000), 1)).toBe(false)
);
