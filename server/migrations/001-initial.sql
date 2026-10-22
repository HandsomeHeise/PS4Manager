CREATE TABLE IF NOT EXISTS admin_users (
  id CHAR(36) PRIMARY KEY,
  username VARCHAR(100) NOT NULL UNIQUE,
  password_hash VARCHAR(255) NOT NULL,
  totp_secret_encrypted VARBINARY(512) NOT NULL,
  state ENUM('active', 'disabled') NOT NULL DEFAULT 'active',
  created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS admin_sessions (
  token_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
  admin_user_id CHAR(36) NOT NULL,
  csrf_token CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  expires_utc DATETIME(6) NOT NULL,
  INDEX ix_session_expiry (expires_utc),
  FOREIGN KEY (admin_user_id) REFERENCES admin_users(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS mods (
  id CHAR(36) PRIMARY KEY,
  slug VARCHAR(160) NOT NULL UNIQUE,
  title VARCHAR(255) NOT NULL,
  author VARCHAR(255) NOT NULL,
  summary VARCHAR(1000) NOT NULL,
  description TEXT NOT NULL,
  category VARCHAR(100) NOT NULL,
  state ENUM('draft', 'published', 'archived') NOT NULL DEFAULT 'draft',
  created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS artifacts (
  id CHAR(36) PRIMARY KEY,
  sha256 CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL UNIQUE,
  storage_key VARCHAR(255) CHARACTER SET ascii COLLATE ascii_bin NOT NULL UNIQUE,
  content_type VARCHAR(100) NOT NULL,
  size_bytes BIGINT UNSIGNED NOT NULL,
  created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS mod_media (
  id CHAR(36) PRIMARY KEY,
  mod_id CHAR(36) NOT NULL,
  artifact_id CHAR(36) NOT NULL,
  kind ENUM('cover', 'screenshot', 'video') NOT NULL,
  position INT UNSIGNED NOT NULL,
  created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  UNIQUE (mod_id, position),
  FOREIGN KEY (mod_id) REFERENCES mods(id) ON DELETE RESTRICT,
  FOREIGN KEY (artifact_id) REFERENCES artifacts(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS mod_releases (
  id CHAR(36) PRIMARY KEY,
  mod_id CHAR(36) NOT NULL,
  version VARCHAR(100) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  package_type ENUM('files', 'luma-script') NOT NULL,
  changelog TEXT NOT NULL,
  artifact_id CHAR(36),
  staged_path VARCHAR(255),
  manifest JSON,
  signed_envelope TEXT,
  signature TEXT,
  signing_key_id CHAR(16) CHARACTER SET ascii COLLATE ascii_bin,
  state ENUM('draft', 'published', 'withdrawn') NOT NULL DEFAULT 'draft',
  withdrawal_reason TEXT,
  created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  published_utc DATETIME(6),
  UNIQUE (mod_id, version),
  FOREIGN KEY (mod_id) REFERENCES mods(id) ON DELETE RESTRICT,
  FOREIGN KEY (artifact_id) REFERENCES artifacts(id) ON DELETE RESTRICT,
  CHECK (state = 'draft' OR (artifact_id IS NOT NULL AND manifest IS NOT NULL AND
    signed_envelope IS NOT NULL AND signature IS NOT NULL AND signing_key_id IS NOT NULL AND published_utc IS NOT NULL))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS release_compatibility (
  id CHAR(36) PRIMARY KEY,
  release_id CHAR(36) NOT NULL,
  title_id CHAR(9) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  game_version VARCHAR(7) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  UNIQUE (release_id, title_id, game_version),
  INDEX ix_compatibility (title_id, game_version),
  FOREIGN KEY (release_id) REFERENCES mod_releases(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS app_releases (
  id CHAR(36) PRIMARY KEY,
  version VARCHAR(100) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  channel ENUM('stable', 'preview') NOT NULL,
  notes TEXT NOT NULL,
  artifact_id CHAR(36) NOT NULL,
  url VARCHAR(2048) NOT NULL,
  signature TEXT NOT NULL,
  signed_envelope TEXT NOT NULL,
  state ENUM('published', 'withdrawn') NOT NULL,
  published_utc DATETIME(6) NOT NULL,
  UNIQUE (channel, version),
  FOREIGN KEY (artifact_id) REFERENCES artifacts(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS audit_entries (
  id CHAR(36) PRIMARY KEY,
  admin_user_id CHAR(36) NOT NULL,
  action VARCHAR(100) NOT NULL,
  subject VARCHAR(255) NOT NULL,
  details JSON NOT NULL,
  created_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  INDEX ix_audit_time (created_utc, id),
  FOREIGN KEY (admin_user_id) REFERENCES admin_users(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
