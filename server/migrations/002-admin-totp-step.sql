SET @totp_step_exists = (SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'admin_users' AND COLUMN_NAME = 'last_totp_step');
SET @totp_step_ddl = IF(@totp_step_exists = 0, 'ALTER TABLE admin_users ADD COLUMN last_totp_step BIGINT UNSIGNED NULL', 'SELECT 1');
PREPARE totp_step_statement FROM @totp_step_ddl;
EXECUTE totp_step_statement;
DEALLOCATE PREPARE totp_step_statement;
