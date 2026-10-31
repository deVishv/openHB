-- The user layer of settings (spec 7): each user's own values, so their settings follow them to any PC. Values are JSON
-- text. The key "keybindings" holds the user's keybindings.json array.
CREATE TABLE user_settings(
    user_id INTEGER NOT NULL REFERENCES users(id),
    key TEXT NOT NULL,
    value TEXT NOT NULL,
    PRIMARY KEY (user_id, key)
) STRICT;
