import { expect, it } from "vitest";
import { verify, createHash, createPublicKey } from "node:crypto";
import { canonicalReleasePayload } from "../../src/domain/release-envelope.js";
import { RsaSigner } from "../../src/publishing/rsa-signer.js";
import { testKeys } from "../helpers/keys.js";
const input = {
  releaseId: "11111111-1111-4111-8111-111111111111",
  modId: "22222222-2222-4222-8222-222222222222",
  version: "2.0.0",
  packageType: "files" as const,
  size: 5,
  sha256: "a".repeat(64),
  compatibility: [
    { titleId: "CUSA00411", gameVersion: "1.58" },
    { titleId: "CUSA00411", gameVersion: "1.57" }
  ]
};
it("pins the canonical signed bytes independently of compatibility input order", () => {
  const expected =
    "PS4MANAGER-MOD-RELEASE-V1\nreleaseId=11111111-1111-4111-8111-111111111111\nmodId=22222222-2222-4222-8222-222222222222\nversion=2.0.0\npackageType=files\nsize=5\nsha256=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\ncompatibility=CUSA00411@1.57,CUSA00411@1.58\n";
  expect(canonicalReleasePayload(input).toString()).toBe(expected);
  expect(
    canonicalReleasePayload({
      ...input,
      compatibility: [...input.compatibility].reverse()
    }).toString()
  ).toBe(expected);
});
it.each([
  { version: "1.0\nsize=9" },
  { size: -1 },
  { sha256: "bad" },
  { releaseId: "invalid" },
  { compatibility: [] }
])("rejects ambiguous signing inputs", (change) => {
  expect(() => canonicalReleasePayload({ ...input, ...change })).toThrow();
});
it("signs with the expected public-key identity and rejects altered payloads", async () => {
  const keys = testKeys(),
    signer = new RsaSigner(keys.privateKey);
  const payload = canonicalReleasePayload(input),
    signed = await signer.sign(payload);
  expect(signed.keyId).toBe(
    createHash("sha256")
      .update(createPublicKey(keys.publicKey).export({ format: "der", type: "spki" }))
      .digest("hex")
      .slice(0, 16)
  );
  expect(
    verify("RSA-SHA256", payload, keys.publicKey, Buffer.from(signed.signatureBase64, "base64"))
  ).toBe(true);
  expect(
    verify(
      "RSA-SHA256",
      canonicalReleasePayload({ ...input, size: 6 }),
      keys.publicKey,
      Buffer.from(signed.signatureBase64, "base64")
    )
  ).toBe(false);
});
