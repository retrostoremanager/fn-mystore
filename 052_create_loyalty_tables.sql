-- Issue #183: Loyalty DB schema — LoyaltySettings and LoyaltyTransactions tables

-- LoyaltySettings: per-company configuration for the loyalty points system
CREATE TABLE IF NOT EXISTS loyalty_settings (
    id                          SERIAL PRIMARY KEY,
    company_id                  INTEGER NOT NULL,
    points_per_dollar_spent     DECIMAL(10, 4) NOT NULL DEFAULT 1,
    points_per_dollar_trade_in  DECIMAL(10, 4) NOT NULL DEFAULT 1,
    redemption_rate             DECIMAL(10, 4) NOT NULL DEFAULT 100,
    is_enabled                  BOOLEAN NOT NULL DEFAULT FALSE,
    CONSTRAINT fk_loyalty_settings_company FOREIGN KEY (company_id) REFERENCES company(id),
    CONSTRAINT uq_loyalty_settings_company UNIQUE (company_id),
    CONSTRAINT chk_loyalty_settings_points_per_dollar_spent CHECK (points_per_dollar_spent >= 0),
    CONSTRAINT chk_loyalty_settings_points_per_dollar_trade_in CHECK (points_per_dollar_trade_in >= 0),
    CONSTRAINT chk_loyalty_settings_redemption_rate CHECK (redemption_rate > 0)
);

-- Index on company_id (also covered by unique constraint, but explicit for clarity)
CREATE INDEX IF NOT EXISTS ix_loyalty_settings_company_id ON loyalty_settings(company_id);

COMMENT ON TABLE loyalty_settings IS 'Per-company configuration for the customer loyalty points system.';
COMMENT ON COLUMN loyalty_settings.points_per_dollar_spent IS 'Number of loyalty points earned per $1 spent on a sale.';
COMMENT ON COLUMN loyalty_settings.points_per_dollar_trade_in IS 'Number of loyalty points earned per $1 value in a trade-in.';
COMMENT ON COLUMN loyalty_settings.redemption_rate IS 'Number of points required to earn $1 of store credit.';
COMMENT ON COLUMN loyalty_settings.is_enabled IS 'Whether the loyalty programme is active for this company.';

-- LoyaltyTransactions: ledger of every points earn and redemption event
CREATE TABLE IF NOT EXISTS loyalty_transaction (
    id                  SERIAL PRIMARY KEY,
    company_id          INTEGER NOT NULL,
    customer_id         INTEGER NOT NULL,
    points              INTEGER NOT NULL,
    transaction_type    VARCHAR(20) NOT NULL,
    reference_id        INTEGER,
    notes               VARCHAR(500),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT fk_loyalty_transaction_company FOREIGN KEY (company_id) REFERENCES company(id),
    CONSTRAINT fk_loyalty_transaction_customer FOREIGN KEY (customer_id) REFERENCES customer(id),
    CONSTRAINT chk_loyalty_transaction_type CHECK (transaction_type IN ('earn_sale', 'earn_tradein', 'redeem')),
    CONSTRAINT chk_loyalty_transaction_points_nonzero CHECK (points <> 0)
);

-- Index on company_id for company-scoped queries
CREATE INDEX IF NOT EXISTS ix_loyalty_transaction_company_id ON loyalty_transaction(company_id);

-- Index on customer_id for customer loyalty history lookups
CREATE INDEX IF NOT EXISTS ix_loyalty_transaction_customer_id ON loyalty_transaction(customer_id);

-- Composite index on (company_id, customer_id) for the common query pattern
CREATE INDEX IF NOT EXISTS ix_loyalty_transaction_company_customer ON loyalty_transaction(company_id, customer_id);

COMMENT ON TABLE loyalty_transaction IS 'Ledger recording every loyalty points earn and redemption event tied to a customer.';
COMMENT ON COLUMN loyalty_transaction.points IS 'Points delta: positive for earn, negative for redeem.';
COMMENT ON COLUMN loyalty_transaction.transaction_type IS 'Type of transaction: earn_sale, earn_tradein, or redeem.';
COMMENT ON COLUMN loyalty_transaction.reference_id IS 'Optional reference to the source record (sale.id or trade_in.id).';
