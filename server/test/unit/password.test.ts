import { expect, it } from "vitest";
import { hashPassword, verifyPassword } from "../../src/auth/password.js";
it("uses separate salts and verifies only the correct password", async () => {
  const password = "correct horse battery staple";
  const first = await hashPassword(password),
    second = await hashPassword(password);
  expect(first).not.toBe(second);
  expect(await verifyPassword(password, first)).toBe(true);
  expect(await verifyPassword("wrong", first)).toBe(false);
});
it.each(["garbage", "scrypt$999999999$8$1$AA==$AA==", "scrypt$16384$8$1$!$!"])(
  "rejects malformed or unsafe password encodings",
  async (encoded) => {
    expect(await verifyPassword("password", encoded)).toBe(false);
  }
);
it("requires a sufficiently long bootstrap password", async () => {
  await expect(hashPassword("short")).rejects.toThrow();
});
