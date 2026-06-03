-- Issue #294: Seed one default LoyaltySettings row per existing company
--
-- The loyalty_settings and loyalty_transaction tables were created in
-- migration 052_create_loyalty_tables.sql. This migration backfills the
-- per-company default row required by issue #294's acceptance criteria
-- so that every existing company has a usable (disabled) loyalty
-- configuration. New companies provisioned after this migration must
-- have their default row inserted by application code.
--
-- Idempotent: ON CONFLICT DO NOTHING relies on the unique constraint
-- uq_loyalty_settings_company (one row per company) defined in migration 052.

INSERT INTO loyalty_settings (
    company_id,
    points_per_dollar_spent,
    points_per_dollar_trade_in,
    redemption_rate,
    is_enabled
)
SELECT
    c.id,
    1,
    1,
    100,
    FALSE
FROM company c
ON CONFLICT ON CONSTRAINT uq_loyalty_settings_company DO NOTHING;
