-- Plan 6: marks the default groups that reports find by role (Schedule III headings, borrowings, fixed assets,
-- investments, depreciation). Default groups have the same ids in every company, so a renamed group keeps its role.
UPDATE groups SET kind = 'Equity' WHERE id = 1 AND kind = 'None' AND parent_id IS NULL;
UPDATE groups SET kind = 'NonCurrentLiabilities' WHERE id = 4 AND kind = 'None' AND parent_id IS NULL;
UPDATE groups SET kind = 'LongTermBorrowings' WHERE id = 5 AND kind = 'None' AND parent_id = 4;
UPDATE groups SET kind = 'CurrentLiabilities' WHERE id = 6 AND kind = 'None' AND parent_id IS NULL;
UPDATE groups SET kind = 'ShortTermBorrowings' WHERE id = 7 AND kind = 'None' AND parent_id = 6;
UPDATE groups SET kind = 'NonCurrentAssets' WHERE id = 13 AND kind = 'None' AND parent_id IS NULL;
UPDATE groups SET kind = 'FixedAssets' WHERE id = 14 AND kind = 'None' AND parent_id = 13;
UPDATE groups SET kind = 'Investments' WHERE id = 15 AND kind = 'None' AND parent_id = 13;
UPDATE groups SET kind = 'CurrentAssets' WHERE id = 16 AND kind = 'None' AND parent_id IS NULL;
UPDATE groups SET kind = 'Income' WHERE id = 24 AND kind = 'None' AND parent_id IS NULL;
UPDATE groups SET kind = 'Expenses' WHERE id = 27 AND kind = 'None' AND parent_id IS NULL;
UPDATE groups SET kind = 'Depreciation' WHERE id = 32 AND kind = 'None' AND parent_id = 27;
