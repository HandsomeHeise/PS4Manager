import { expect, it } from "vitest";
import {
  normalizeTitleId,
  normalizeGameVersion,
  compatibilityKey
} from "../../src/domain/compatibility.js";
it.each([
  ["cusa00411", "CUSA00411"],
  [" CUSA00411 ", "CUSA00411"]
])("normalizes title %s", (input, result) => expect(normalizeTitleId(input)).toBe(result));
it.each(["CUSA0411", "CUSA0041X", "CUSA00411\n", "", "CUSΑ00411"])("rejects title %j", (input) =>
  expect(() => normalizeTitleId(input)).toThrow()
);
it.each([
  ["01.57", "1.57"],
  ["1.0", "1.00"],
  [" 1.29 ", "1.29"],
  ["999.999", "999.999"]
])("normalizes version %s", (input, result) => expect(normalizeGameVersion(input)).toBe(result));
it.each(["1.2.3", "1e2.1", "-1.00", "1000.0", "1.1000", "1 .00", "1.00\n", "+1.0", ""])(
  "rejects version %j",
  (input) => expect(() => normalizeGameVersion(input)).toThrow()
);
it("keeps compatibility exact", () => {
  expect(compatibilityKey("cusa00411", "01.57")).toBe("CUSA00411@1.57");
  expect(compatibilityKey("CUSA00411", "1.58")).not.toBe("CUSA00411@1.57");
});
