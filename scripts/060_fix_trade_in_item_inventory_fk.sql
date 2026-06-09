-- Issue #310 / #311: Fix trade_in_item.inventory_item_id foreign key reference.
--
-- Migration 048_create_trade_in_tables.sql created the FK constraint
-- fk_trade_in_item_inventory_item against inventory_item(id). However,
-- migration 034_rename_game_to_game_encyclopedia_inventory_to_game_inventory.sql
-- renamed inventory_item to game_inventory. After migration 048 runs against
-- a database where the rename has already been applied, the FK is left
-- pointing at a table name that no longer matches the live table, so every
-- attempt to set trade_in_item.inventory_item_id to a real game_inventory.id
-- raises 23503 (foreign key violation). This breaks
-- POST /api/trade-ins/{id}/complete for every code path that links a
-- trade-in item to inventory (both merge and create paths from PR #170).
--
-- PR #177 added this migration at the repo root, but the deploy-function-app.yml
-- workflow only applies migrations from the scripts/ directory
-- (`for f in $(ls scripts/*.sql | sort)`). The migration was therefore never
-- applied to dev. This PR places the migration in scripts/ so it is picked up
-- by the deploy workflow on the next deployment to dev.
--
-- This migration drops the broken constraint and re-adds it against the
-- correct table, game_inventory(id). The constraint is renamed to
-- fk_trade_in_item_game_inventory so the new name reflects the actual
-- referenced table.
--
-- Idempotent: uses IF EXISTS / IF NOT EXISTS where supported. The DROP is
-- guarded by IF EXISTS so this migration is safe to re-run.

ALTER TABLE trade_in_item
    DROP CONSTRAINT IF EXISTS fk_trade_in_item_inventory_item;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_trade_in_item_game_inventory'
          AND conrelid = 'trade_in_item'::regclass
    ) THEN
        ALTER TABLE trade_in_item
            ADD CONSTRAINT fk_trade_in_item_game_inventory
            FOREIGN KEY (inventory_item_id) REFERENCES game_inventory(id);
    END IF;
END $$;

COMMENT ON COLUMN trade_in_item.inventory_item_id IS
    'Optional link to a game_inventory row if this trade-in item was added to inventory. Originally referenced inventory_item, which was renamed to game_inventory in migration 034.';
