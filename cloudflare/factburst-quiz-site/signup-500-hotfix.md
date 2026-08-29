Hotfix verification notes:
- upgrades legacy site_users email columns sequentially before dependent account schema objects
- account schema helper is idempotent and covered by accounts.test.mjs
- worker returns a safe account_schema_error response if schema preparation still fails
