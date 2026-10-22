import assert from "node:assert/strict";
import { execFile, spawn } from "node:child_process";
import { promisify } from "node:util";
import { mkdtemp, mkdir, writeFile, readFile, readdir, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import {
  createHash,
  createHmac,
  randomBytes,
  randomUUID,
  createPublicKey,
  verify,
  constants
} from "node:crypto";
import https from "node:https";
import process from "node:process";
import { setTimeout } from "node:timers";
import { URL } from "node:url";
import { Buffer } from "node:buffer";
import { buildZip } from "./build-zips.ts";
const execute = promisify(execFile),
  directory = await mkdtemp(join(tmpdir(), "ps4manager-compose-"));
const project = "ps4manager-test-" + randomUUID().slice(0, 8),
  restored = project + "-restore";
const secrets = join(directory, "secrets"),
  tls = join(directory, "tls"),
  offsite = join(directory, "offsite");
for (const path of [secrets, tls, offsite]) await mkdir(path);
const configPath = resolve("compose.yaml"),
  override = join(directory, "override.json"),
  restoreOverride = join(directory, "restore.json");
const env = {
  ...process.env,
  SECRETS_DIRECTORY: secrets,
  TLS_DIRECTORY: tls,
  ACME_DIRECTORY: directory,
  PUBLIC_BASE_URL: "https://mods.example.test",
  SERVER_NAME: "mods.example.test",
  RCLONE_REMOTE: "/offsite"
};
async function docker(args, options = {}) {
  return execute("docker", args, { env, maxBuffer: 8 * 1024 * 1024, ...options });
}
function compose(name, args, restore = false) {
  return docker([
    "compose",
    "-p",
    name,
    "-f",
    override,
    ...(restore ? ["-f", restoreOverride] : []),
    ...args
  ]);
}
async function healthy(name, services, restore = false) {
  for (let i = 0; i < 90; i++) {
    const { stdout } = await compose(name, ["ps", "--format", "json"], restore);
    const rows = stdout
      .trim()
      .split("\n")
      .filter(Boolean)
      .map((line) => JSON.parse(line));
    if (
      services.every((service) => rows.some((r) => r.Service === service && r.Health === "healthy"))
    )
      return;
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }
  throw new Error("Containers did not become healthy");
}
let port,
  cookies = new Map();
async function request(path, { method = "GET", payload, headers = {} } = {}) {
  const bytes =
    payload === undefined
      ? undefined
      : Buffer.isBuffer(payload)
        ? payload
        : Buffer.from(JSON.stringify(payload));
  return new Promise((resolve, reject) => {
    const req = https.request(
      {
        hostname: "127.0.0.1",
        port,
        path,
        method,
        rejectUnauthorized: false,
        headers: {
          host: "mods.example.test",
          cookie: [...cookies].map(([k, v]) => k + "=" + v).join("; "),
          ...(bytes ? { "content-type": "application/json", "content-length": bytes.length } : {}),
          ...headers
        }
      },
      (res) => {
        for (const line of res.headers["set-cookie"] || []) {
          const [pair] = line.split(";");
          const index = pair.indexOf("=");
          cookies.set(pair.slice(0, index), pair.slice(index + 1));
        }
        const chunks = [];
        res.on("data", (chunk) => chunks.push(chunk));
        res.on("end", () => {
          const body = Buffer.concat(chunks);
          resolve({
            status: res.statusCode,
            headers: res.headers,
            body,
            json: () => JSON.parse(body.toString("utf8"))
          });
        });
      }
    );
    req.on("error", reject);
    req.end(bytes);
  });
}
function multipart(parts) {
  return Buffer.concat(
    parts
      .flatMap(([name, filename, mime, bytes]) => [
        Buffer.from(
          `--probe\r\nContent-Disposition: form-data; name="${name}"; filename="${filename}"\r\nContent-Type: ${mime}\r\n\r\n`
        ),
        bytes,
        Buffer.from("\r\n")
      ])
      .concat(Buffer.from("--probe--\r\n"))
  );
}
try {
  for (const name of ["mysql_password", "mysql_root_password"])
    await writeFile(join(secrets, name), randomBytes(32).toString("hex"));
  for (const name of ["session_secret", "totp_key"])
    await writeFile(join(secrets, name), randomBytes(32));
  const fixture = JSON.parse(await readFile("test/fixtures/update-verification.json", "utf8"));
  await writeFile(join(secrets, "update_public_key.pem"), fixture.publicKeyPem);
  await writeFile(join(secrets, "rclone.conf"), "");
  await docker([
    "run",
    "--rm",
    "--entrypoint",
    "openssl",
    "-v",
    `${secrets}:/secrets`,
    "ps4manager-mods-backup",
    "genpkey",
    "-algorithm",
    "RSA",
    "-pkeyopt",
    "rsa_keygen_bits:2048",
    "-out",
    "/secrets/mod_private_key.pem"
  ]);
  const signingPublicKey = await docker([
    "run", "--rm", "--entrypoint", "openssl", "-v", `${secrets}:/secrets`,
    "ps4manager-mods-backup", "pkey", "-in", "/secrets/mod_private_key.pem", "-pubout"
  ]);
  await docker([
    "run",
    "--rm",
    "--entrypoint",
    "chown",
    "-v",
    `${secrets}:/secrets`,
    "ps4manager-mods-backup",
    "1000:1000",
    "/secrets/mod_private_key.pem"
  ]);
  await docker([
    "run",
    "--rm",
    "--entrypoint",
    "openssl",
    "-v",
    `${tls}:/tls`,
    "ps4manager-mods-backup",
    "req",
    "-x509",
    "-newkey",
    "rsa:2048",
    "-nodes",
    "-keyout",
    "/tls/privkey.pem",
    "-out",
    "/tls/fullchain.pem",
    "-days",
    "1",
    "-subj",
    "/CN=mods.example.test"
  ]);
  const smokeConfig = {
    services: {
      mysql: { healthcheck: { interval: "1s", retries: 90 } },
      api: { image: "ps4manager-mods-api:latest", healthcheck: { interval: "1s", retries: 90 } },
      backup: {
        image: "ps4manager-mods-backup:latest",
        volumes: [`${offsite}:/offsite`],
        healthcheck: { interval: "1s", retries: 90 }
      },
      nginx: { ports: [], healthcheck: { interval: "1s", retries: 90 } }
    }
  };
  // Compose's !override tag is YAML-only; isolate test ports from host services.
  await writeFile(override, JSON.stringify(smokeConfig));
  const yamlOverride = join(directory, "ports.yaml");
  await writeFile(
    yamlOverride,
    'services:\n  nginx:\n    ports: !override\n      - "127.0.0.1::443"\n      - "127.0.0.1::80"\n'
  );
  // Apply ports through the generated JSON graph as the final test config.
  const { stdout: resolved } = await docker([
    "compose",
    "-p",
    project,
    "-f",
    configPath,
    "-f",
    override,
    "-f",
    yamlOverride,
    "config",
    "--format",
    "json"
  ]);
  const graph = JSON.parse(resolved);
  delete graph.name;
  for (const volume of Object.values(graph.volumes)) delete volume.name;
  for (const network of Object.values(graph.networks)) delete network.name;
  await writeFile(override, JSON.stringify(graph));
  await writeFile(
    restoreOverride,
    JSON.stringify({
      services: {
        backup: { volumes: [`${offsite}:/backups:ro`, "artifact_data:/var/lib/ps4manager"] }
      }
    })
  );
  await compose(project, ["up", "-d", "--no-build"]);
  await healthy(project, ["mysql", "api", "nginx", "backup"]);
  port = Number((await compose(project, ["port", "nginx", "443"])).stdout.trim().split(":").pop());
  assert.equal((await request("/health/ready")).status, 200);
  assert.equal(
    (await request("/_artifacts/packages/mods/aa/" + "a".repeat(64) + ".zip")).status,
    404
  );
  const seed = `import {createPool} from './dist/db/pool.js';import {loadConfig} from './dist/config.js';import {hashPassword} from './dist/auth/password.js';import {SecretBox} from './dist/auth/secret-box.js';import {readFile} from 'node:fs/promises';import {randomUUID} from 'node:crypto';const c=loadConfig(process.env),p=createPool(c.database);await p.execute('INSERT INTO admin_users (id,username,password_hash,totp_secret_encrypted) VALUES (?,?,?,?)',[randomUUID(),'smoke',await hashPassword('smoke test password only'),new SecretBox(await readFile(c.totpEncryptionKeyFile)).seal(Buffer.from('12345678901234567890'))]);await p.end();`;
  await compose(project, ["exec", "-T", "api", "node", "--input-type=module", "-e", seed]);
  const pre = (await request("/api/admin/login-token")).json();
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30000)));
  const mac = createHmac("sha1", "12345678901234567890").update(counter).digest(),
    offset = mac.at(-1) & 15,
    totp = String((mac.readUInt32BE(offset) & 0x7fffffff) % 1000000).padStart(6, "0");
  const login = await request("/api/admin/login", {
    method: "POST",
    headers: { "x-csrf-token": pre.csrfToken },
    payload: { username: "smoke", password: "smoke test password only", totp }
  });
  assert.equal(login.status, 200);
  const csrf = login.json().csrfToken;
  async function mutation(path, payload, upload = false) {
    const result = await request(path, {
      method: "POST",
      payload,
      headers: {
        "x-csrf-token": csrf,
        ...(upload ? { "content-type": "multipart/form-data; boundary=probe" } : {})
      }
    });
    assert.ok(
      result.status >= 200 && result.status < 300,
      `${path}: ${result.status} ${result.body}`
    );
    return result.json();
  }
  const mod = await mutation("/api/admin/mods", {
    slug: "smoke-mod",
    title: "Smoke mod",
    author: "Test",
    summary: "Test",
    description: "Test",
    category: "test"
  });
  const preview = Buffer.concat([Buffer.from("89504e470d0a1a0a", "hex"), Buffer.alloc(32)]);
  await mutation(
    `/api/admin/mods/${mod.id}/media?kind=cover&position=0`,
    multipart([["file", "cover.png", "image/png", preview]]),
    true
  );
  const release = await mutation(`/api/admin/mods/${mod.id}/releases`, {
    version: "1.0",
    packageType: "files",
    changelog: "Test",
    compatibility: [{ titleId: "CUSA00411", gameVersion: "1.57" }]
  });
  const zip = buildZip([
    {
      name: "manifest.json",
      content: JSON.stringify({
        formatVersion: 1,
        packageType: "files",
        operations: [{ source: "payload/test.txt", target: "/data/smoke/test.txt" }]
      })
    },
    { name: "payload/test.txt", content: "Smoke payload" }
  ]);
  await mutation(
    `/api/admin/releases/${release.id}/package`,
    multipart([["file", "mod.zip", "application/zip", zip]]),
    true
  );
  assert.match(
    (await request(`/admin/releases/${release.id}/review`)).body.toString(),
    /\/data\/smoke\/test.txt/u
  );
  await mutation(`/api/admin/releases/${release.id}/publish`, {});
  const catalog = "/api/v1/catalog?titleId=CUSA00411&gameVersion=1.57";
  assert.equal((await request(catalog)).json().items.length, 1);
  const download = `/api/v1/releases/${release.id}/download`;
  assert.deepEqual((await request(download)).body, zip);
  const detail = (await request(`/api/v1/releases/${release.id}`)).json();
  const hash = createHash("sha256").update(zip).digest("hex");
  const payload = Buffer.from(
    [
      "PS4MANAGER-MOD-RELEASE-V1",
      `releaseId=${release.id}`,
      `modId=${mod.id}`,
      "version=1.0",
      "packageType=files",
      `size=${zip.length}`,
      `sha256=${hash}`,
      "compatibility=CUSA00411@1.57",
      ""
    ].join("\n")
  );
  assert.equal(detail.signedEnvelope, payload.toString("utf8"));
  assert.equal(
    verify(
      "RSA-SHA256",
      payload,
      {
        key: createPublicKey(signingPublicKey.stdout),
        padding: constants.RSA_PKCS1_PADDING
      },
      Buffer.from(detail.signature, "base64")
    ),
    true
  );
  await mutation(`/api/admin/releases/${release.id}/withdraw`, { reason: "Smoke test" });
  assert.equal((await request(catalog)).json().items.length, 0);
  assert.equal((await request(download)).status, 404);
  await mutation(`/api/admin/releases/${release.id}/restore`, {});
  assert.deepEqual((await request(download)).body, zip);
  const manifest = await readFile("test/fixtures/dotnet-update-manifest.json");
  await mutation(
    "/api/admin/updates",
    multipart([
      ["manifest", "manifest.json", "application/json", manifest],
      ["package", "update.zip", "application/zip", Buffer.from(fixture.packageBase64, "base64")]
    ]),
    true
  );
  assert.equal((await request("/api/v1/updates/latest?channel=stable")).json().version, "9.8.7");
  assert.equal((await request("/api/v1/updates/latest?channel=preview")).status, 404);
  const packagePath = new URL(JSON.parse(manifest.toString()).url).pathname;
  assert.equal(
    createHash("sha256")
      .update((await request(packagePath)).body)
      .digest("hex"),
    JSON.parse(manifest.toString()).sha256
  );
  const auditCheck =
    "import {createPool} from './dist/db/pool.js';import {loadConfig} from './dist/config.js';const p=createPool(loadConfig(process.env).database);const [rows]=await p.query('SELECT action FROM audit_entries ORDER BY created_utc,id');console.log(JSON.stringify(rows.map(r=>r.action)));await p.end();";
  const audit = JSON.parse(
    (
      await compose(project, ["exec", "-T", "api", "node", "--input-type=module", "-e", auditCheck])
    ).stdout.trim()
  );
  assert.deepEqual(audit, [
    "admin.login",
    "mod.create",
    "media.upload",
    "release.create",
    "release.upload",
    "release.publish",
    "release.withdraw",
    "release.restore",
    "update.publish"
  ]);
  // Separate container origins traverse the real TLS/Nginx boundary. Changing
  // forged forwarding headers must not reset client A's quota or block client B.
  for (const [path, method, maximum, allowed] of [
    ["/api/admin/login", "POST", 5, 403],
    ["/api/admin/login-token", "GET", 30, 200]
  ]) {
    const probe = (count) => `
      import https from 'node:https';
      const statuses=[];
      for(let i=0;i<${count};i++) statuses.push(await new Promise((resolve,reject)=>{
        const req=https.request({hostname:'nginx',port:443,path:${JSON.stringify(path)},
          method:${JSON.stringify(method)},rejectUnauthorized:false,
          headers:{'x-forwarded-for':'203.0.113.'+(i+1)}},res=>{
          res.resume();res.on('end',()=>resolve(res.statusCode));
        });req.on('error',reject);req.end();
      }));console.log(JSON.stringify(statuses));`;
    const first = await compose(project, [
      "exec",
      "-T",
      "api",
      "node",
      "--input-type=module",
      "-e",
      probe(maximum + 1)
    ]);
    assert.deepEqual(JSON.parse(first.stdout.trim()), [...Array(maximum).fill(allowed), 429]);
    const second = await compose(project, [
      "exec",
      "-T",
      "backup",
      "node",
      "--input-type=module",
      "-e",
      probe(1)
    ]);
    assert.deepEqual(JSON.parse(second.stdout.trim()), [allowed]);
  }
  const lockCode =
    "import {createPool} from './dist/db/pool.js';import {loadConfig} from './dist/config.js';const p=createPool(loadConfig(process.env).database),c=await p.getConnection();await c.query(\"SELECT GET_LOCK('ps4manager-artifacts',30)\");console.log('LOCKED');await new Promise(resolve=>process.stdin.once('data',resolve));await c.query(\"SELECT RELEASE_LOCK('ps4manager-artifacts')\");c.release();await p.end();";
  const locker = spawn(
    "docker",
    [
      "compose",
      "-p",
      project,
      "-f",
      override,
      "exec",
      "-T",
      "api",
      "node",
      "--input-type=module",
      "-e",
      lockCode
    ],
    { env, stdio: ["pipe", "pipe", "pipe"] }
  );
  locker.stderr.resume();
  const unlocked = new Promise((resolve, reject) => {
    locker.once("error", reject);
    locker.once("close", (code) =>
      code === 0 ? resolve() : reject(new Error("Lock probe failed"))
    );
  });
  let backup;
  try {
    await new Promise((resolve, reject) => {
      locker.stdout.on("data", (data) => {
        if (data.toString().includes("LOCKED")) resolve();
      });
      locker.once("error", reject);
      locker.once("exit", () => reject(new Error("Lock probe exited before readiness")));
    });
    backup = compose(project, ["exec", "-T", "backup", "/app/backup/backup.sh"]);
    const completedWhileLocked = await Promise.race([
      backup.then(() => true),
      new Promise((resolve) => setTimeout(() => resolve(false), 5000))
    ]);
    assert.equal(
      completedWhileLocked,
      false,
      "Backup must wait while publication/cleanup owns the storage lock"
    );
  } finally {
    locker.stdin.end("release\n");
    await unlocked;
    await backup;
  }
  const archive = (await readdir(offsite))
    .filter((n) => n.endsWith(".tar.gz"))
    .sort()
    .at(-1);
  assert.ok(archive);
  await compose(restored, ["up", "-d", "--no-build", "mysql"], true);
  await healthy(restored, ["mysql"], true);
  const restoreArgs = [
    "run",
    "--rm",
    "--no-deps",
    "-e",
    "RESTORE_WRITERS_STOPPED=1",
    "backup",
    "node",
    "/app/backup/restore.mjs",
    archive
  ];
  await compose(restored, restoreArgs, true);
  await assert.rejects(compose(restored, [...restoreArgs, "--replace-empty-volumes"], true));
  await compose(restored, ["up", "-d", "--no-build", "api", "nginx"], true);
  await healthy(restored, ["mysql", "api", "nginx"], true);
  port = Number(
    (await compose(restored, ["port", "nginx", "443"], true)).stdout.trim().split(":").pop()
  );
  assert.equal((await request(catalog)).json().items.length, 1);
  assert.deepEqual((await request(download)).body, zip);
  assert.equal((await request("/api/admin/session")).status, 200);
  await compose(
    restored,
    [
      "exec",
      "-T",
      "api",
      "node",
      "-e",
      "require('fs').accessSync('/var/lib/ps4manager/packages/mods',require('fs').constants.W_OK)"
    ],
    true
  );
  process.stdout.write(
    "PASS: HTTPS admin/login, publish/download/withdraw/restore, update fixture, offsite-copy backup and fresh-volume restore; overwrite refused.\n"
  );
} catch (error) {
  try {
    const diagnostic = await compose(project, [
      "run",
      "--rm",
      "--no-deps",
      "api",
      "node",
      "--input-type=module",
      "-e",
      "import {createProductionApp} from './dist/production.js';import {loadConfig} from './dist/config.js';try{const app=await createProductionApp({config:loadConfig(process.env),clock:{now:()=>new Date()}});await app.close();console.log('Factory diagnostic passed');}catch(e){console.log(e.code,e.message);}"
    ]);
    process.stderr.write(diagnostic.stdout);
  } catch {
    /* Startup diagnostics are best-effort on a failed test stack. */
  }
  for (const name of [project, restored]) {
    try {
      const result = await compose(name, ["logs", "--tail", "25"], name === restored);
      process.stderr.write(result.stdout);
    } catch {
      /* A project may not have been created yet. */
    }
  }
  throw error;
} finally {
  for (const name of [restored, project])
    await compose(name, ["down", "-v", "--remove-orphans"], name === restored).catch(
      () => undefined
    );
  await rm(directory, { recursive: true, force: true });
}
