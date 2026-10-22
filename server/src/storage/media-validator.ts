import { open } from "node:fs/promises";
export interface PreviewMetadata {
  contentType: string;
  extension: "png" | "jpg" | "mp4";
  size: number;
}
export async function validatePreview(
  path: string,
  declaredType: string,
  maxBytes: number
): Promise<PreviewMetadata> {
  const file = await open(path, "r");
  try {
    const { size } = await file.stat();
    if (!Number.isSafeInteger(maxBytes) || maxBytes <= 0 || size === 0 || size > maxBytes)
      throw new Error("Preview size limit exceeded");
    const buffer = Buffer.alloc(32),
      { bytesRead } = await file.read(buffer, 0, 32, 0);
    const header = buffer.subarray(0, bytesRead);
    let extension: PreviewMetadata["extension"] | undefined;
    if (
      declaredType === "image/png" &&
      header.subarray(0, 8).equals(Buffer.from("89504e470d0a1a0a", "hex"))
    )
      extension = "png";
    if (
      declaredType === "image/jpeg" &&
      header[0] === 0xff &&
      header[1] === 0xd8 &&
      header[2] === 0xff
    )
      extension = "jpg";
    if (
      declaredType === "video/mp4" &&
      bytesRead >= 16 &&
      header.toString("ascii", 4, 8) === "ftyp" &&
      header.readUInt32BE(0) >= 16 &&
      header.readUInt32BE(0) <= size &&
      ["isom", "iso2", "mp41", "mp42", "avc1", "M4V "].includes(header.toString("ascii", 8, 12))
    )
      extension = "mp4";
    if (!extension) throw new Error("Preview signature does not match declared MIME type");
    return { contentType: declaredType, extension, size };
  } finally {
    await file.close();
  }
}
