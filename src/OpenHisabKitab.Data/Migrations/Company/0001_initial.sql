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

CREATE TABLE company_profile(
    id INTEGER PRIMARY KEY CHECK (id = 1),
    name TEXT NOT NULL,
    state_code TEXT NOT NULL,
    registration TEXT NOT NULL,
    gstin TEXT NULL,
    address TEXT NOT NULL,
    first_fy_start INTEGER NOT NULL
);

CREATE TABLE financial_years(
    start_year INTEGER PRIMARY KEY
);

CREATE TABLE company_settings(
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

CREATE TABLE groups(
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL,
    parent_id INTEGER NULL REFERENCES groups(id),
    nature TEXT NOT NULL,
    kind TEXT NOT NULL
);

CREATE TABLE ledgers(
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL,
    group_id INTEGER NOT NULL REFERENCES groups(id),
    required TEXT NULL UNIQUE,
    opening_paise INTEGER NOT NULL DEFAULT 0,
    row_version INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE parties(
    ledger_id INTEGER PRIMARY KEY REFERENCES ledgers(id),
    gstin TEXT NULL,
    state_code TEXT NOT NULL,
    credit_days INTEGER NOT NULL,
    address TEXT NOT NULL,
    row_version INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE items(
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL UNIQUE COLLATE NOCASE,
    hsn_sac TEXT NOT NULL,
    is_service INTEGER NOT NULL,
    unit TEXT NOT NULL,
    rate_bp INTEGER NOT NULL,
    cess_bp INTEGER NOT NULL,
    default_price_paise INTEGER NOT NULL,
    row_version INTEGER NOT NULL DEFAULT 1
);
