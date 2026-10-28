-- The newest audit record of each company file, kept outside that file. Removing the newest records of a company, or
-- rebuilding its whole chain, no longer matches this row.
CREATE TABLE audit_anchors(
    company_id INTEGER PRIMARY KEY,
    seq INTEGER NOT NULL,
    hash TEXT NOT NULL
);
