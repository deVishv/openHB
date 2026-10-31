-- Plan 5: export invoice fields, save request ids, party opening bills, and bill by bill allocations (spec 13).
ALTER TABLE invoices ADD COLUMN port_code TEXT NULL;
ALTER TABLE invoices ADD COLUMN shipping_bill_number TEXT NULL;
ALTER TABLE invoices ADD COLUMN shipping_bill_date TEXT NULL;
-- A client's id for one save, so a retried save returns the first voucher instead of making a second.
ALTER TABLE vouchers ADD COLUMN request_id TEXT NULL;
CREATE UNIQUE INDEX vouchers_request_id ON vouchers(request_id) WHERE request_id IS NOT NULL;
CREATE INDEX invoices_party ON invoices(party_ledger_id);
-- Opening bills have their own version, so saving a party and then its bills never conflicts with itself.
ALTER TABLE parties ADD COLUMN opening_bills_version INTEGER NOT NULL DEFAULT 1;

CREATE TABLE opening_bills(
    id INTEGER PRIMARY KEY,
    party_ledger_id INTEGER NOT NULL REFERENCES parties(ledger_id),
    number TEXT NOT NULL,
    date TEXT NOT NULL,
    due_date TEXT NULL,
    amount_paise INTEGER NOT NULL CHECK (amount_paise > 0)
);
CREATE UNIQUE INDEX opening_bills_number ON opening_bills(party_ledger_id, number COLLATE NOCASE);

-- One row per bill a settling voucher (receipt, payment, credit or debit note) settles. Exactly one bill column is set.
CREATE TABLE allocations(
    voucher_id INTEGER NOT NULL REFERENCES vouchers(id),
    party_ledger_id INTEGER NOT NULL REFERENCES ledgers(id),
    bill_voucher_id INTEGER NULL REFERENCES vouchers(id),
    opening_bill_id INTEGER NULL REFERENCES opening_bills(id),
    amount_paise INTEGER NOT NULL CHECK (amount_paise > 0),
    CHECK ((bill_voucher_id IS NULL) <> (opening_bill_id IS NULL))
);
CREATE INDEX allocations_voucher ON allocations(voucher_id);
CREATE INDEX allocations_bill_voucher ON allocations(bill_voucher_id);
CREATE INDEX allocations_opening_bill ON allocations(opening_bill_id);
