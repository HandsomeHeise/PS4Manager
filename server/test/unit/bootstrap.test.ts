import { expect, it } from "vitest";
import { enrollmentUri, createAdminCli } from "../../src/auth/bootstrap.js";
it("encodes a standard base32 TOTP enrollment URI", () => {
  const uri = new URL(enrollmentUri("admin", Buffer.from("12345678901234567890")));
  expect(uri.protocol).toBe("otpauth:");
  expect(uri.searchParams.get("secret")).toBe("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ");
  expect(uri.searchParams.get("issuer")).toBe("PS4Manager");
  expect(uri.searchParams.get("digits")).toBe("6");
});
it("refuses noninteractive admin creation before loading secrets", async () => {
  await expect(createAdminCli()).rejects.toThrow("interactive terminal");
});
