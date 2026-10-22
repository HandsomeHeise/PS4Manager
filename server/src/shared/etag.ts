import { createHash } from "node:crypto";
export function etag(value: unknown): string {
  return `"${createHash("sha256").update(JSON.stringify(value), "utf8").digest("hex")}"`;
}
