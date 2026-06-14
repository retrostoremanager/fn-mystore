-- Issue #320: Persist promotion discounts on sale and sale_item.
--
-- SalesService.CreateSale now calls IPromotionService.ApplyPromotionsAsync
-- before computing totals so that promoted line items are stored at their
-- discounted price. To keep receipts accurate and let GET /sales/{id} surface
-- the promotion that was applied, the application writes the following new
-- columns on insert and reads them back on select.
--
-- This migration belongs in retrostoremanager/dbproj-mystore/PostgreSQL/ and
-- must be deployed before the corresponding fn-mystore release. A copy is
-- kept here for visibility alongside the application change.

ALTER TABLE sale
    ADD COLUMN IF NOT EXISTS discount_total DECIMAL(18, 2) NOT NULL DEFAULT 0;

ALTER TABLE sale_item
    ADD COLUMN IF NOT EXISTS discount_amount DECIMAL(18, 2) NOT NULL DEFAULT 0;

ALTER TABLE sale_item
    ADD COLUMN IF NOT EXISTS promotion_id INTEGER;

ALTER TABLE sale_item
    ADD COLUMN IF NOT EXISTS promotion_name VARCHAR(200);

-- promotion_id is not enforced with a FK because a promotion may be deleted
-- after the sale completes; the column captures the historical association
-- and falls back to promotion_name for display.
CREATE INDEX IF NOT EXISTS ix_sale_item_promotion_id
    ON sale_item(promotion_id)
    WHERE promotion_id IS NOT NULL;

COMMENT ON COLUMN sale.discount_total IS 'Sum of per-line promotion discounts applied to this sale.';
COMMENT ON COLUMN sale_item.discount_amount IS 'Promotion discount applied to this line, in store currency.';
COMMENT ON COLUMN sale_item.promotion_id IS 'Promotion that produced the discount, if any. Not FK-enforced.';
COMMENT ON COLUMN sale_item.promotion_name IS 'Snapshot of the promotion name at sale time.';
