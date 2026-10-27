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
-- A new row must extend the head. This also stops INSERT OR REPLACE, which skips delete triggers.
CREATE TRIGGER audit_log_insert_at_head BEFORE INSERT ON audit_log BEGIN
    SELECT RAISE(ABORT, 'audit log is append only')
    WHERE NEW.seq <> COALESCE((SELECT max(seq) FROM audit_log), 0) + 1
       OR NEW.prev_hash <> COALESCE((SELECT hash FROM audit_log ORDER BY seq DESC LIMIT 1), '0000000000000000000000000000000000000000000000000000000000000000');
END;

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

CREATE TABLE number_series(
    type TEXT NOT NULL,
    series TEXT NOT NULL,
    fy_start INTEGER NOT NULL REFERENCES financial_years(start_year),
    pattern TEXT NOT NULL,
    width INTEGER NOT NULL,
    next INTEGER NOT NULL,
    PRIMARY KEY(type, series, fy_start)
);

CREATE TABLE vouchers(
    id INTEGER PRIMARY KEY,
    type TEXT NOT NULL,
    series TEXT NOT NULL,
    fy_start INTEGER NOT NULL,
    number INTEGER NOT NULL,
    display_number TEXT NOT NULL,
    date TEXT NOT NULL,
    narration TEXT NOT NULL,
    cancelled INTEGER NOT NULL DEFAULT 0,
    created_by INTEGER NOT NULL,
    row_version INTEGER NOT NULL DEFAULT 1,
    UNIQUE(type, series, fy_start, number)
);
CREATE INDEX vouchers_date ON vouchers(date);

CREATE TABLE postings(
    id INTEGER PRIMARY KEY,
    voucher_id INTEGER NOT NULL REFERENCES vouchers(id),
    date TEXT NOT NULL,
    ledger_id INTEGER NOT NULL REFERENCES ledgers(id),
    amount_paise INTEGER NOT NULL
);
CREATE INDEX postings_ledger_date ON postings(ledger_id, date);
CREATE INDEX postings_voucher ON postings(voucher_id);

CREATE TABLE invoices(
    voucher_id INTEGER PRIMARY KEY REFERENCES vouchers(id),
    party_ledger_id INTEGER NOT NULL REFERENCES ledgers(id),
    direction TEXT NOT NULL,
    kind TEXT NOT NULL,
    place_of_supply TEXT NOT NULL,
    reverse_charge INTEGER NOT NULL,
    party_doc_number TEXT NULL,
    party_doc_date TEXT NULL,
    original_number TEXT NULL,
    original_date TEXT NULL,
    round_off_paise INTEGER NOT NULL
);

CREATE TABLE invoice_lines(
    voucher_id INTEGER NOT NULL REFERENCES invoices(voucher_id),
    line_no INTEGER NOT NULL,
    item_id INTEGER NULL REFERENCES items(id),
    ledger_id INTEGER NOT NULL REFERENCES ledgers(id),
    description TEXT NOT NULL,
    hsn_sac TEXT NOT NULL,
    qty_thousandths INTEGER NOT NULL,
    unit_price_paise INTEGER NOT NULL,
    discount_paise INTEGER NOT NULL,
    rate_bp INTEGER NOT NULL,
    cess_bp INTEGER NOT NULL,
    price_includes_tax INTEGER NOT NULL,
    taxable_paise INTEGER NOT NULL,
    cgst_paise INTEGER NOT NULL,
    sgst_paise INTEGER NOT NULL,
    utgst_paise INTEGER NOT NULL,
    igst_paise INTEGER NOT NULL,
    cess_paise INTEGER NOT NULL,
    PRIMARY KEY(voucher_id, line_no)
);

CREATE TABLE period_lock(
    id INTEGER PRIMARY KEY CHECK (id = 1),
    locked_up_to TEXT NULL
);
