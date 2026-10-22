import { loadConfig } from "../../src/config.js";
export function testConfig() {
  return loadConfig({
    NODE_ENV: "test",
    HOST: "127.0.0.1",
    PORT: "3100",
    PUBLIC_BASE_URL: "https://mods.example.test",
    MYSQL_HOST: "localhost",
    MYSQL_PORT: "3306",
    MYSQL_DATABASE: "ps4manager",
    MYSQL_USER: "ps4manager",
    MYSQL_PASSWORD_FILE: "/run/secrets/mysql_password",
    STORAGE_ROOT: "/var/lib/ps4manager",
    SESSION_SECRET_FILE: "/run/secrets/session",
    TOTP_ENCRYPTION_KEY_FILE: "/run/secrets/totp",
    MOD_PRIVATE_KEY_FILE: "/run/secrets/mod",
    UPDATE_PUBLIC_KEY_FILE: "/run/secrets/update"
  });
}
