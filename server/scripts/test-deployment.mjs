import { spawn } from "node:child_process";
import process from "node:process";
const child = spawn(
  process.execPath,
  ["node_modules/vitest/vitest.mjs", "run", "test/integration/end-to-end.integration.test.ts"],
  { env: { ...process.env, RUN_COMPOSE_E2E: "1" }, stdio: "inherit" }
);
child.once("error", () => {
  process.exitCode = 1;
});
child.once("close", (code) => {
  process.exitCode = code ?? 1;
});
