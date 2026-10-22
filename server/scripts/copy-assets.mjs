import { cp } from "node:fs/promises";
import { URL } from "node:url";
await cp(
  new URL("../src/admin/views/", import.meta.url),
  new URL("../dist/admin/views/", import.meta.url),
  { recursive: true }
);
