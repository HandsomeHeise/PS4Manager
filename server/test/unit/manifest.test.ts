import { expect, it } from "vitest";
import { parseManifest } from "../../src/domain/manifest.js";
const manifest = {
  formatVersion: 1,
  packageType: "files",
  operations: [{ source: "payload/config.ini", target: "/data/example/config.ini" }]
};
it("parses an exact files manifest", () =>
  expect(parseManifest(JSON.stringify(manifest))).toEqual(manifest));
it.each([
  "../escape",
  "payload/a/../b",
  "payload//b",
  "payload/a\\b",
  "payload/CON",
  "payload/aux.txt",
  "payload/COM¹.txt",
  "payload/LpT²/nested.lua",
  "payload/com³",
  `payload/${"a".repeat(256)}`,
  `payload/${"é".repeat(128)}`,
  "payload/a.",
  "payload/file:stream",
  "payload/a\u0000b",
  "payload/"
])("rejects unsafe source %j", (source) => {
  expect(() =>
    parseManifest(JSON.stringify({ ...manifest, operations: [{ source, target: "/data/x" }] }))
  ).toThrow();
});
it.each(["/system/x", "/data", "/data//x", "/data/../system/x", "data/x", "/data/x/", "/datax/x"])(
  "rejects unsafe target %j",
  (target) => {
    expect(() =>
      parseManifest(JSON.stringify({ ...manifest, operations: [{ source: "payload/x", target }] }))
    ).toThrow();
  }
);
it.each([
  { ...manifest, surprise: true },
  { ...manifest, formatVersion: 2 },
  { ...manifest, operations: [] },
  { ...manifest, operations: [{ ...manifest.operations[0], extra: true }] },
  {
    ...manifest,
    operations: [
      manifest.operations[0],
      { source: "payload/other", target: "/data/example/CONFIG.ini" }
    ]
  },
  {
    ...manifest,
    operations: [manifest.operations[0], { source: "payload/CONFIG.ini", target: "/data/other" }]
  }
])("rejects malformed or colliding manifests", (input) =>
  expect(() => parseManifest(JSON.stringify(input))).toThrow()
);
it.each([
  ["gta5", "/data/gtalua/scripts/a.lua"],
  ["rdr2", "/data/rdr2lua/scripts/a.lua"]
])("accepts the %s script root", (lumaProfile, target) => {
  expect(
    parseManifest(
      JSON.stringify({
        ...manifest,
        packageType: "luma-script",
        lumaProfile,
        operations: [{ source: "payload/a.lua", target }]
      })
    ).packageType
  ).toBe("luma-script");
});
it("rejects a script outside its selected profile", () => {
  expect(() =>
    parseManifest(JSON.stringify({ ...manifest, packageType: "luma-script", lumaProfile: "gta5" }))
  ).toThrow();
});
it("accepts a component at the 255 UTF-8 byte boundary", () => {
  const name = "é".repeat(127) + "a";
  expect(() =>
    parseManifest(
      JSON.stringify({
        ...manifest,
        operations: [{ source: `payload/${name}`, target: `/data/${name}` }]
      })
    )
  ).not.toThrow();
});
it.each(["COM¹.txt", "LPT²/nested.lua", "a".repeat(256), "é".repeat(128)])(
  "rejects nonportable destination %j",
  (name) => {
    expect(() =>
      parseManifest(
        JSON.stringify({
          ...manifest,
          operations: [{ source: "payload/x", target: `/data/${name}` }]
        })
      )
    ).toThrow();
  }
);
