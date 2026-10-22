import { HttpError } from "../shared/http-errors.js";
export interface UpdateManifest {
  version: string;
  channel: string;
  url: string;
  sha256: string;
  notes: string;
  signature: string;
}
export function versionParts(value: string): number[] {
  if (!/^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$/u.test(value))
    throw new HttpError(400, "invalid_version");
  const parts = value.split(".").map(Number);
  if (parts.some((n) => !Number.isInteger(n) || n > 2147483647))
    throw new HttpError(400, "invalid_version");
  return parts;
}
export function compareVersions(a: string, b: string): number {
  const x = versionParts(a),
    y = versionParts(b);
  for (let i = 0; i < 3; i++) if (x[i] !== y[i]) return x[i]! - y[i]!;
  return 0;
}
export function parseUpdateManifest(value: unknown): UpdateManifest {
  if (!value || typeof value !== "object" || Array.isArray(value))
    throw new HttpError(400, "invalid_manifest");
  const v = value as Record<string, unknown>,
    fields = ["version", "channel", "url", "sha256", "notes", "signature"];
  if (Object.keys(v).length !== fields.length || fields.some((k) => typeof v[k] !== "string"))
    throw new HttpError(400, "invalid_manifest");
  const m = v as unknown as UpdateManifest;
  versionParts(m.version);
  if (
    !["stable", "preview"].includes(m.channel) ||
    !/^[a-f0-9]{64}$/u.test(m.sha256) ||
    m.notes.length > 16000 ||
    m.url.length > 2048 ||
    m.signature.length > 2048
  )
    throw new HttpError(400, "invalid_manifest");
  try {
    const url = new URL(m.url);
    if (
      url.protocol !== "https:" ||
      url.username ||
      url.password ||
      url.hash ||
      url.search ||
      url.href !== m.url
    )
      throw new Error();
  } catch {
    throw new HttpError(400, "invalid_update_url");
  }
  return {
    version: m.version,
    channel: m.channel,
    url: m.url,
    sha256: m.sha256,
    notes: m.notes,
    signature: m.signature
  };
}
export function signedUpdatePayload(m: UpdateManifest): Buffer {
  return Buffer.from([m.version, m.channel, m.url, m.sha256, m.notes].join("\n"), "utf8");
}
