-- Issue #358 (orchestrator-mystore): Trade-in DB schema retry.
--
-- Re-asserts the trade-in tables, indexes, and permission seeds defined by
-- migrations 048, 049, 050, and 058 in a single idempotent migration so the
-- schema can be re-verified on any environment without manual intervention.
--
-- This migration intentionally:
--   * does NOT make trade_in.payment_type NOT NULL again. The acceptance
--     criteria text says NOT NULL, but draft trade-ins genuinely do not have
--     a payment type until they are completed (POST /trade-ins/{id}/complete).
--     Migration 050 already relaxed the constraint to match the application
--     contract (TradeInService / TradeInRepository) and the C# model
--     (`string? PaymentType`). Re-tightening it here would break draft
--     creation. The CHECK constraint still restricts non-null values to
--     'cash' or 'store_credit'.
--   * does NOT re-assign trade_in.complete to the Employee role. Migration
--     058 removed it to match the parent issue (#278) which only grants
--     trade_in.create and trade_in.view to Employee. Owner and Manager
--     retain all three permissions.
--
-- All statements below are safe to re-run on a schema where 048 / 049 / 050 /
-- 058 have already been applied.

-- 1. Tables --------------------------------------------------------------

CREATE TABLE IF NOT EXISTS trade_in (
    id                      SERIAL PRIMARY KEY,
    company_id              INTEGER NOT NULL,
    customer_id             INTEGER,
    status                  VARCHAR(50) NOT NULL DEFAULT 'draft',
    total_offered_value     DECIMAL(18, 2) NOT NULL DEFAULT 0,
    total_accepted_value    DECIMAL(18, 2),
    payment_type            VARCHAR(50),
    notes                   TEXT,
    created_by              INTEGER NOT NULL,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at            TIMESTAMPTZ,
    CONSTRAINT fk_trade_in_company FOREIGN KEY (company_id) REFERENCES company(id),
    CONSTRAINT fk_trade_in_customer FOREIGN KEY (customer_id) REFERENCES customer(id),
    CONSTRAINT fk_trade_in_created_by FOREIGN KEY (created_by) REFERENCES "user"(id),
    CONSTRAINT chk_trade_in_status CHECK (status IN ('draft', 'completed', 'rejected')),
    CONSTRAINT chk_trade_in_payment_type CHECK (payment_type IS NULL OR payment_type IN ('cash', 'store_credit')),
    CONSTRAINT chk_trade_in_total_offered_value CHECK (total_offered_value >= 0),
    CONSTRAINT chk_trade_in_total_accepted_value CHECK (total_accepted_value IS NULL OR total_accepted_value >= 0)
);

CREATE TABLE IF NOT EXISTS trade_in_item (
    id                  SERIAL PRIMARY KEY,
    trade_in_id         INTEGER NOT NULL,
    game_title          VARCHAR(500) NOT NULL,
    platform            VARCHAR(100) NOT NULL,
    condition           VARCHAR(50) NOT NULL,
    offered_value       DECIMAL(18, 2) NOT NULL,
    accepted_value      DECIMAL(18, 2),
    inventory_item_id   INTEGER,
    parsed_by_ai        BOOLEAN NOT NULL DEFAULT false,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT fk_trade_in_item_trade_in FOREIGN KEY (trade_in_id) REFERENCES trade_in(id) ON DELETE CASCADE,
    CONSTRAINT chk_trade_in_item_condition CHECK (condition IN ('poor', 'fair', 'good', 'excellent')),
    CONSTRAINT chk_trade_in_item_offered_value CHECK (offered_value >= 0),
    CONSTRAINT chk_trade_in_item_accepted_value CHECK (accepted_value IS NULL OR accepted_value >= 0)
);

-- 2. Indexes required by the acceptance criteria -------------------------
-- Named exactly as called out by issue #358 (ix_trade_ins_company,
-- ix_trade_in_items_trade_in). The earlier compound indexes from 048
-- (ix_trade_in_company_status, ix_trade_in_company_created_at) are kept
-- because they support list queries; the single-column indexes added here
-- are cheap and satisfy the AC verbatim.

CREATE INDEX IF NOT EXISTS ix_trade_ins_company ON trade_in(company_id);
CREATE INDEX IF NOT EXISTS ix_trade_in_items_trade_in ON trade_in_item(trade_in_id);

-- 3. Permission seeds ----------------------------------------------------
-- Guarded so this only runs when the user/role/permission schema (031)
-- has been applied. The acceptance criteria asks us to verify these tables
-- exist before seeding.

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.tables
                   WHERE table_schema = 'public' AND table_name = 'permission') THEN
        RAISE NOTICE 'permission table not found; skipping trade-in permission seeds';
        RETURN;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.tables
                   WHERE table_schema = 'public' AND table_name = 'role_permission') THEN
        RAISE NOTICE 'role_permission table not found; skipping trade-in permission seeds';
        RETURN;
    END IF;

    INSERT INTO permission (name, description) VALUES
        ('trade_in.create', 'Create and manage trade-in transactions'),
        ('trade_in.view', 'View trade-in transactions and items'),
        ('trade_in.complete', 'Complete or reject trade-in transactions')
    ON CONFLICT (name) DO NOTHING;

    -- Owner: all three trade-in permissions
    INSERT INTO role_permission (role_id, permission_id)
    SELECT r.id, p.id
    FROM role r CROSS JOIN permission p
    WHERE r.company_id IS NULL AND r.name = 'Owner'
      AND p.name IN ('trade_in.create', 'trade_in.view', 'trade_in.complete')
    ON CONFLICT (role_id, permission_id) DO NOTHING;

    -- Manager: all three trade-in permissions
    INSERT INTO role_permission (role_id, permission_id)
    SELECT r.id, p.id
    FROM role r CROSS JOIN permission p
    WHERE r.company_id IS NULL AND r.name = 'Manager'
      AND p.name IN ('trade_in.create', 'trade_in.view', 'trade_in.complete')
    ON CONFLICT (role_id, permission_id) DO NOTHING;

    -- Employee: create + view only (matches parent issue #278; complete is
    -- intentionally withheld from Employee by migration 058).
    INSERT INTO role_permission (role_id, permission_id)
    SELECT r.id, p.id
    FROM role r CROSS JOIN permission p
    WHERE r.company_id IS NULL AND r.name = 'Employee'
      AND p.name IN ('trade_in.create', 'trade_in.view')
    ON CONFLICT (role_id, permission_id) DO NOTHING;
END
$$;
