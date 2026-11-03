-- Plan 6b: Ship to on issued documents (Rule 46(o) and the export proviso), the buyer's and company's details as they
-- were when the invoice was saved, so a reprint matches the original, and the company logo printed on invoices.
ALTER TABLE invoices ADD COLUMN ship_to_name TEXT NULL;
ALTER TABLE invoices ADD COLUMN ship_to_address TEXT NULL;
ALTER TABLE invoices ADD COLUMN ship_to_state TEXT NULL;
ALTER TABLE invoices ADD COLUMN ship_to_country TEXT NULL;
ALTER TABLE invoices ADD COLUMN party_name TEXT NULL;
ALTER TABLE invoices ADD COLUMN party_gstin TEXT NULL;
ALTER TABLE invoices ADD COLUMN party_address TEXT NULL;
ALTER TABLE invoices ADD COLUMN party_state TEXT NULL;
ALTER TABLE invoices ADD COLUMN company_name TEXT NULL;
ALTER TABLE invoices ADD COLUMN company_gstin TEXT NULL;
ALTER TABLE invoices ADD COLUMN company_address TEXT NULL;
ALTER TABLE invoices ADD COLUMN company_state TEXT NULL;

CREATE TABLE company_logo(
    id INTEGER PRIMARY KEY CHECK (id = 1),
    png BLOB NOT NULL,
    sha256 TEXT NOT NULL
);
