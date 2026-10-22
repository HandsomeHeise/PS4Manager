export interface PackageOperation {
  source: string;
  target: string;
}
export type PackageManifest = {
  formatVersion: 1;
  operations: PackageOperation[];
} & ({ packageType: "files" } | { packageType: "luma-script"; lumaProfile: "gta5" | "rdr2" });
const roots = [
  "/data",
  ...Array.from({ length: 8 }, (_, i) => `/mnt/usb${i}`),
  "/user/av_contents",
  "/user/home",
  "/user/app",
  "/user/patch",
  "/user/addcont",
  "/user/appmeta",
  "/mnt/ext0/user"
];
const scriptRoots = { gta5: "/data/gtalua/scripts", rdr2: "/data/rdr2lua/scripts" };

export function validateRelativePath(value: string): void {
  if (!value || value.length > 1024 || value.normalize("NFC") !== value)
    throw new Error("Invalid path");
  for (const part of value.split("/")) {
    if (
      !part ||
      Buffer.byteLength(part, "utf8") > 255 ||
      part === "." ||
      part === ".." ||
      // Control characters are deliberately forbidden in portable package paths.
      // eslint-disable-next-line no-control-regex
      /[\\<>:"|?*\u0000-\u001f\u007f]/u.test(part) ||
      /[. ]$/u.test(part) ||
      /^(con|prn|aux|nul|com[1-9¹²³]|lpt[1-9¹²³])(?:\.|$)/iu.test(part)
    ) {
      throw new Error("Unsafe path segment");
    }
  }
}
function object(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value))
    throw new Error("Expected object");
  return value as Record<string, unknown>;
}
function exact(value: Record<string, unknown>, keys: string[]): void {
  if (Object.keys(value).length !== keys.length || keys.some((key) => !Object.hasOwn(value, key)))
    throw new Error("Unexpected manifest fields");
}
export function parseManifest(json: string): PackageManifest {
  const value = object(JSON.parse(json));
  const isLuma = value.packageType === "luma-script";
  exact(value, ["formatVersion", "packageType", "operations", ...(isLuma ? ["lumaProfile"] : [])]);
  if (value.formatVersion !== 1 || (!isLuma && value.packageType !== "files"))
    throw new Error("Unsupported package format");
  const profile = value.lumaProfile;
  if (isLuma && profile !== "gta5" && profile !== "rdr2") throw new Error("Unknown Luma profile");
  if (
    !Array.isArray(value.operations) ||
    value.operations.length === 0 ||
    value.operations.length > 100_000
  )
    throw new Error("Invalid operation count");
  const sources = new Set<string>(),
    targets = new Set<string>();
  const operations = value.operations.map((item: unknown) => {
    const op = object(item);
    exact(op, ["source", "target"]);
    if (typeof op.source !== "string" || typeof op.target !== "string")
      throw new Error("Invalid operation");
    validateRelativePath(op.source);
    if (!op.source.startsWith("payload/")) throw new Error("Source outside payload");
    if (!op.target.startsWith("/")) throw new Error("Target must be absolute");
    validateRelativePath(op.target.slice(1));
    const target = op.target;
    if (!roots.some((root) => target.startsWith(root + "/")))
      throw new Error("Forbidden PS4 target");
    if (
      isLuma &&
      (profile === "gta5" || profile === "rdr2") &&
      !op.target.startsWith(scriptRoots[profile] + "/")
    )
      throw new Error("Target outside Luma profile root");
    for (const [seen, path] of [
      [sources, op.source],
      [targets, op.target]
    ] as const) {
      const folded = path.toLowerCase();
      if (seen.has(folded)) throw new Error("Colliding package paths");
      seen.add(folded);
    }
    return { source: op.source, target: op.target };
  });
  // File/directory prefix conflicts cannot be installed reliably either.
  for (const paths of [sources, targets])
    for (const path of paths) {
      const parts = path.split("/");
      for (let i = 1; i < parts.length; i++)
        if (paths.has(parts.slice(0, i).join("/"))) throw new Error("File/directory path conflict");
    }
  if (isLuma && (profile === "gta5" || profile === "rdr2"))
    return { formatVersion: 1, packageType: "luma-script", lumaProfile: profile, operations };
  return { formatVersion: 1, packageType: "files", operations };
}
