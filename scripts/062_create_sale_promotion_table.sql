-- Issue #322: Promotions — return applied discounts breakdown in GET /sales responses.
--
-- Issue #320 added discount columns directly on sale_item (one promotion per
-- line). This migration adds a dedicated sale_promotion table that captures
-- every promotion applied to a sale alongside its aggregate discount amount,
-- which lets GET /sales/{id} and the receipt endpoint surface a
-- per-promotion savings breakdown even when multiple promotions stack on the
-- same sale.
--
-- This migration belongs in retrostoremanager/dbproj-mystore/PostgreSQL/ and
-- must be deployed before the corresponding fn-mystore release. A copy is
-- kept here for visibility alongside the application change.

CREATE TABLE IF NOT EXISTS sale_promotion (
    id              SERIAL PRIMARY KEY,
    sale_id         INTEGER NOT NULL,
    promotion_id    INTEGER NOT NULL,
    promotion_name  VARCHAR(200) NOT NULL,
    discount_amount DECIMAL(18, 2) NOT NULL,
    created_date    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT fk_sale_promotion_sale FOREIGN KEY (sale_id) REFERENCES sale(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_sale_promotion_sale_id ON sale_promotion(sale_id);
CREATE INDEX IF NOT EXISTS ix_sale_promotion_promotion_id ON sale_promotion(promotion_id);

COMMENT ON TABLE sale_promotion IS 'Per-promotion discount breakdown applied to a sale; supports multiple promotions per sale.';
COMMENT ON COLUMN sale_promotion.sale_id IS 'Sale this promotion applied to.';
COMMENT ON COLUMN sale_promotion.promotion_id IS 'Promotion that was applied. Not FK-enforced because the promotion may be deleted after the sale completes.';
COMMENT ON COLUMN sale_promotion.promotion_name IS 'Snapshot of the promotion name at sale time.';
COMMENT ON COLUMN sale_promotion.discount_amount IS 'Total discount amount this promotion contributed to the sale.';
