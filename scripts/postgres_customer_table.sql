-- Run against the MyStore PostgreSQL database (same as game_inventory / "user").
-- Stores tenant customers with optional email (phone-only rows allowed).

CREATE TABLE IF NOT EXISTS customer (
    id                  SERIAL PRIMARY KEY,
    company_id          INTEGER NOT NULL,
    first_name          VARCHAR(100) NOT NULL,
    last_name           VARCHAR(100) NOT NULL DEFAULT '',
    email               VARCHAR(255) NULL,
    phone               VARCHAR(20) NULL,
    address             VARCHAR(255) NULL,
    city                VARCHAR(100) NULL,
    state               VARCHAR(50) NULL,
    zip_code            VARCHAR(10) NULL,
    created_date        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_modified_date  TIMESTAMPTZ NULL
);

CREATE INDEX IF NOT EXISTS ix_customer_company_id ON customer (company_id);

CREATE UNIQUE INDEX IF NOT EXISTS ix_customer_company_email
    ON customer (company_id, email)
    WHERE email IS NOT NULL;
