import { expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { verify, constants, createHash } from "node:crypto";
import {
  parseUpdateManifest,
  signedUpdatePayload,
  compareVersions
} from "../../src/updates/update-manifest.js";
const manifest = {
  version: "1.2.0",
  channel: "stable",
  url: "https://mods.example.test/package.zip",
  sha256: "a".repeat(64),
  notes: "Changed things\nZweite Zeile ä",
  signature: "ignored"
};
it("matches the .NET five-line UTF8 payload without adding a trailing newline", () => {
  expect(signedUpdatePayload(manifest).toString("utf8")).toBe(
    "1.2.0\nstable\nhttps://mods.example.test/package.zip\n" +
      "a".repeat(64) +
      "\nChanged things\nZweite Zeile ä"
  );
});
it.each(["1.2.0-preview", "01.2.0", "1.2", "1.2.2147483648", "1.2.0\nstable"])(
  "rejects unsupported client version %s",
  (version) => {
    expect(() => parseUpdateManifest({ ...manifest, version })).toThrow();
  }
);
it("compares version components numerically", () => {
  expect(compareVersions("1.10.0", "1.9.99")).toBeGreaterThan(0);
  expect(compareVersions("2.0.0", "10.0.0")).toBeLessThan(0);
});
it("verifies the frozen cross-runtime fixture against its exact ZIP bytes", () => {
  const manifest = parseUpdateManifest(
    JSON.parse(
      readFileSync(new URL("../fixtures/dotnet-update-manifest.json", import.meta.url), "utf8")
    )
  );
  const fixture = JSON.parse(
    readFileSync(new URL("../fixtures/update-verification.json", import.meta.url), "utf8")
  );
  expect(
    verify(
      "RSA-SHA256",
      signedUpdatePayload(manifest),
      { key: fixture.publicKeyPem, padding: constants.RSA_PKCS1_PADDING },
      Buffer.from(manifest.signature, "base64")
    )
  ).toBe(true);
  expect(
    createHash("sha256").update(Buffer.from(fixture.packageBase64, "base64")).digest("hex")
  ).toBe(manifest.sha256);
});
