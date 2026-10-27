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
