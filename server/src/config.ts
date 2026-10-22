export type AppEnvironment = "development" | "test" | "production";

export interface DatabaseConfig {
  host: string;
  port: number;
  database: string;
  user: string;
  passwordFile: string;
}

export interface AppConfig {
  environment: AppEnvironment;
  host: string;
  port: number;
  publicBaseUrl: URL;
  database: DatabaseConfig;
  storageRoot: string;
  sessionSecretFile: string;
  totpEncryptionKeyFile: string;
  modPrivateKeyFile: string;
  updatePublicKeyFile: string;
  sessionTtlMinutes: number;
  maxPackageBytes: number;
  maxExpandedBytes: number;
  maxZipEntries: number;
  maxPreviewBytes: number;
  logRetentionDays: number;
}

interface NumericSetting {
  name: string;
  defaultValue?: number;
  maximum: number;
}

function required(env: NodeJS.ProcessEnv, name: string): string {
  const value = env[name]?.trim();
  if (!value) {
    throw new Error(`${name} is required`);
  }
  return value;
}

function positiveInteger(env: NodeJS.ProcessEnv, setting: NumericSetting): number {
  const raw = env[setting.name];
  const value =
    raw === undefined && setting.defaultValue !== undefined ? setting.defaultValue : Number(raw);

  if (!Number.isSafeInteger(value) || value <= 0 || value > setting.maximum) {
    throw new Error(
      `${setting.name} must be a positive integer no greater than ${setting.maximum}`
    );
  }

  return value;
}

function environment(env: NodeJS.ProcessEnv): AppEnvironment {
  const value = required(env, "NODE_ENV");
  if (value !== "development" && value !== "test" && value !== "production") {
    throw new Error("NODE_ENV must be development, test, or production");
  }
  return value;
}

function publicBaseUrl(env: NodeJS.ProcessEnv, appEnvironment: AppEnvironment): URL {
  let value: URL;
  try {
    value = new URL(required(env, "PUBLIC_BASE_URL"));
  } catch {
    throw new Error("PUBLIC_BASE_URL must be an absolute URL");
  }

  if (appEnvironment === "production" && value.protocol !== "https:") {
    throw new Error("PUBLIC_BASE_URL must use HTTPS in production");
  }
  if (value.username || value.password || value.search || value.hash || value.pathname !== "/") {
    throw new Error(
      "PUBLIC_BASE_URL must be an origin without credentials, path, query, or fragment"
    );
  }

  return value;
}

export function loadConfig(env: NodeJS.ProcessEnv): AppConfig {
  const appEnvironment = environment(env);

  return {
    environment: appEnvironment,
    host: required(env, "HOST"),
    port: positiveInteger(env, { name: "PORT", maximum: 65_535 }),
    publicBaseUrl: publicBaseUrl(env, appEnvironment),
    database: {
      host: required(env, "MYSQL_HOST"),
      port: positiveInteger(env, { name: "MYSQL_PORT", maximum: 65_535 }),
      database: required(env, "MYSQL_DATABASE"),
      user: required(env, "MYSQL_USER"),
      passwordFile: required(env, "MYSQL_PASSWORD_FILE")
    },
    storageRoot: required(env, "STORAGE_ROOT"),
    sessionSecretFile: required(env, "SESSION_SECRET_FILE"),
    totpEncryptionKeyFile: required(env, "TOTP_ENCRYPTION_KEY_FILE"),
    modPrivateKeyFile: required(env, "MOD_PRIVATE_KEY_FILE"),
    updatePublicKeyFile: required(env, "UPDATE_PUBLIC_KEY_FILE"),
    sessionTtlMinutes: positiveInteger(env, {
      name: "SESSION_TTL_MINUTES",
      defaultValue: 30,
      maximum: 1_440
    }),
    maxPackageBytes: positiveInteger(env, {
      name: "MAX_PACKAGE_BYTES",
      defaultValue: 2_147_483_648,
      maximum: 8_589_934_592
    }),
    maxExpandedBytes: positiveInteger(env, {
      name: "MAX_EXPANDED_BYTES",
      defaultValue: 4_294_967_296,
      maximum: 17_179_869_184
    }),
    maxZipEntries: positiveInteger(env, {
      name: "MAX_ZIP_ENTRIES",
      defaultValue: 10_000,
      maximum: 100_000
    }),
    maxPreviewBytes: positiveInteger(env, {
      name: "MAX_PREVIEW_BYTES",
      defaultValue: 52_428_800,
      maximum: 1_073_741_824
    }),
    logRetentionDays: positiveInteger(env, {
      name: "LOG_RETENTION_DAYS",
      defaultValue: 14,
      maximum: 3_650
    })
  };
}
