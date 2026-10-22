import assert from "node:assert/strict";
import { Readable } from "node:stream";
import { readFile, readdir, stat } from "node:fs/promises";
import { spawnSync } from "node:child_process";
import process from "node:process";
import { ArtifactStore } from "/app/storage/artifact-store.js";

// Run in an ephemeral Linux Node container with dist mounted at /app.
const store = new ArtifactStore("/tmp/artifacts");
const inputs = await Promise.all([1, 2].map(() => store.stage(Readable.from(["abc"]), "x.zip", 3)));
assert.equal((await stat("/tmp/artifacts/staging")).mode & 0o777, 0o700);
const [a, b] = await Promise.all(inputs.map((input) => store.publish(input, "mods")));
assert.deepEqual(a, b);
const path = "/tmp/artifacts/" + a.storageKey;
assert.equal((await stat(path)).mode & 0o777, 0o644);
assert.equal(await readFile(path, "utf8"), "abc");
assert.deepEqual(await readdir("/tmp/artifacts/staging"), []);
const reader = spawnSync(
  process.execPath,
  ["-e", "require('node:fs').readFileSync(process.argv[1])", path],
  { uid: 65534, gid: 65534 }
);
assert.equal(reader.status, 0, reader.stderr?.toString());
process.stdout.write("Linux atomic publication and independent reader permissions: PASS\n");
