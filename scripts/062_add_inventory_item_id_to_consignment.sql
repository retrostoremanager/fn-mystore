-- Issue #321: Add optional inventory_item_id link to consignment_item so that
-- marking a consignment item sold can decrement the linked inventory row.
-- Nullable to preserve existing rows and support consignments not tracked in inventory.

ALTER TABLE consignment_item
    ADD COLUMN IF NOT EXISTS inventory_item_id INTEGER;

ALTER TABLE consignment_item DROP CONSTRAINT IF EXISTS fk_consignment_item_inventory;
ALTER TABLE consignment_item
    ADD CONSTRAINT fk_consignment_item_inventory
    FOREIGN KEY (inventory_item_id) REFERENCES inventory_item(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS ix_consignment_item_inventory_item_id
    ON consignment_item(inventory_item_id);
