import { it, expect } from "vitest";
import { execFileSync } from "node:child_process";
it("resolves a Compose graph exposing only nginx and isolating signing secrets", () => {
  const config = JSON.parse(
    execFileSync("docker", ["compose", "-f", "compose.yaml", "config", "--format", "json"], {
      encoding: "utf8"
    })
  );
  expect(
    Object.entries(config.services)
      .filter(([, service]) => (service as { ports?: unknown[] }).ports?.length)
      .map(([name]) => name)
  ).toEqual(["nginx"]);
  expect(config.services.mysql.image).toBe("mysql:8.4");
  expect(config.services.api.read_only).toBe(true);
  expect(
    config.services.api.volumes.some((v: { target: string }) => v.target === "/var/lib/ps4manager")
  ).toBe(true);
  expect(
    config.services.mysql.volumes.some((v: { target: string }) => v.target === "/var/lib/mysql")
  ).toBe(true);
  expect(config.services.backup.secrets.map((s: { source: string }) => s.source).sort()).toEqual([
    "mysql_password",
    "rclone_config"
  ]);
  expect(config.networks.backend.internal).toBe(true);
});
it("uses only journald-supported options with the production retention override", () => {
  const config = JSON.parse(
    execFileSync(
      "docker",
      [
        "compose",
        "-f",
        "compose.yaml",
        "-f",
        "compose.journald.yaml",
        "config",
        "--format",
        "json"
      ],
      { encoding: "utf8" }
    )
  );
  for (const service of Object.values(config.services))
    expect((service as { logging: unknown }).logging).toEqual({
      driver: "journald",
      options: { tag: "ps4manager" }
    });
});
