import { it, expect } from "vitest";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
// Requires the deployment images built by Task9; kept explicit so ordinary
// source-only test runs never silently consume a stale image.
it.skipIf(process.env.RUN_COMPOSE_E2E !== "1")(
  "verifies HTTPS publication, immutable bytes, offline updates and backup/restore in isolated Compose stacks",
  async () => {
    const { stdout } = await promisify(execFile)(
      process.execPath,
      ["--import", "tsx", "test/fixtures/compose-smoke.mjs"],
      { maxBuffer: 16 * 1024 * 1024, timeout: 420000 }
    );
    expect(stdout).toContain("PASS: HTTPS admin/login");
  },
  450000
);
