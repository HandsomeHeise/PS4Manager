import { compatibilityKey } from "./compatibility.js";
import { validateId } from "./ids.js";
export interface ReleaseEnvelopeInput {
  releaseId: string;
  modId: string;
  version: string;
  packageType: "files" | "luma-script";
  size: number;
  sha256: string;
  compatibility: { titleId: string; gameVersion: string }[];
}
export function canonicalReleasePayload(input: ReleaseEnvelopeInput): Buffer {
  validateId(input.releaseId);
  validateId(input.modId);
  if (
    !/^[0-9A-Za-z][0-9A-Za-z.+-]{0,99}$/u.test(input.version) ||
    !["files", "luma-script"].includes(input.packageType) ||
    !Number.isSafeInteger(input.size) ||
    input.size <= 0 ||
    !/^[a-f0-9]{64}$/u.test(input.sha256) ||
    input.compatibility.length === 0 ||
    input.compatibility.length > 1000
  )
    throw new Error("Invalid signing input");
  const compatibility = input.compatibility
    .map((pair) => compatibilityKey(pair.titleId, pair.gameVersion))
    .sort();
  if (new Set(compatibility).size !== compatibility.length)
    throw new Error("Duplicate compatibility");
  return Buffer.from(
    [
      "PS4MANAGER-MOD-RELEASE-V1",
      `releaseId=${input.releaseId}`,
      `modId=${input.modId}`,
      `version=${input.version}`,
      `packageType=${input.packageType}`,
      `size=${input.size}`,
      `sha256=${input.sha256}`,
      `compatibility=${compatibility.join(",")}`,
      ""
    ].join("\n"),
    "utf8"
  );
}
