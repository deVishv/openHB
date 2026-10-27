CREATE TABLE app_info(key TEXT PRIMARY KEY, value TEXT NOT NULL);

CREATE TABLE audit_log(
    seq INTEGER PRIMARY KEY,
    at TEXT NOT NULL,
    user TEXT NOT NULL,
    machine TEXT NOT NULL,
    source TEXT NOT NULL,
    token_name TEXT NULL,
    action TEXT NOT NULL,
    entity TEXT NOT NULL,
    entity_key TEXT NOT NULL,
    before TEXT NULL,
    after TEXT NULL,
    prev_hash TEXT NOT NULL,
    hash TEXT NOT NULL
);
CREATE INDEX audit_log_at ON audit_log(at);
CREATE TRIGGER audit_log_no_update BEFORE UPDATE ON audit_log BEGIN SELECT RAISE(ABORT, 'audit log is append only'); END;
CREATE TRIGGER audit_log_no_delete BEFORE DELETE ON audit_log BEGIN SELECT RAISE(ABORT, 'audit log is append only'); END;

CREATE TABLE users(
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL UNIQUE COLLATE NOCASE,
    password_hash TEXT NOT NULL,
    role TEXT NOT NULL,
    active INTEGER NOT NULL DEFAULT 1,
    row_version INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE companies(
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL,
    file_name TEXT NOT NULL UNIQUE
);

CREATE TABLE user_companies(
    user_id INTEGER NOT NULL REFERENCES users(id),
    company_id INTEGER NOT NULL REFERENCES companies(id),
    PRIMARY KEY(user_id, company_id)
);

CREATE TABLE host_settings(
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

-- Used from Plan 3 (local REST API and MCP). Created now so that plan needs no host.db migration.
CREATE TABLE api_tokens(
    id INTEGER PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id),
    name TEXT NOT NULL,
    token_hash TEXT NOT NULL UNIQUE,
    read_write INTEGER NOT NULL,
    created_at TEXT NOT NULL,
    last_used_at TEXT NULL,
    revoked_at TEXT NULL
);
