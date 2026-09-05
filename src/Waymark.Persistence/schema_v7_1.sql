-- =============================================================================
-- Waymark — operational store database
-- File: waymark-store.db
-- Schema version: v7
-- Generated: 04/09/2026
-- =============================================================================
--
-- SCOPE
--   This file creates the store-side operational database only.
--   The identity file (waymark-identity.db) and the cloud Postgres schema
--   are separate and NOT created here.
--
-- CONVENTIONS
--   Identifiers   TEXT, ULID generated in application code. Never autoincrement.
--   Money         INTEGER, in centimes (scale 100). Never REAL, never TEXT.
--                 Mapped to decimal in C# through the Money value object.
--   Quantity      INTEGER, in thousandths (scale 1000). Supports kg to the gram.
--                 Mapped to decimal in C# through the Quantity value object.
--   Percentages   INTEGER, in basis points (scale 10000). 1900 = 19.00%.
--   Timestamps    TEXT, ISO-8601 UTC, 'YYYY-MM-DD HH:MM:SS'.
--   Dates         TEXT, 'YYYY-MM-DD'.
--   Booleans      INTEGER 0 or 1, with a CHECK constraint.
--   Enums         TEXT, with a CHECK constraint listing allowed values.
--
-- STRICT TABLES
--   Every table is declared STRICT (SQLite 3.37+). This makes the decimal
--   discipline enforceable by the database rather than by convention: a REAL
--   cannot be written into an INTEGER column.
--   If EF Core migrations later fight this, STRICT can be dropped without any
--   other change. Nothing else in this file depends on it.
--
-- CONNECTION PRAGMAS
--   foreign_keys must be set ON for EVERY connection. It is not persisted.
--   In Microsoft.Data.Sqlite, add "Foreign Keys=True" to the connection string.
--
-- =============================================================================

PRAGMA journal_mode = WAL;          -- persisted; readers never block the writer
PRAGMA foreign_keys = ON;           -- NOT persisted; set per connection
PRAGMA synchronous = NORMAL;        -- safe under WAL, much faster than FULL
PRAGMA busy_timeout = 5000;

BEGIN;

-- =============================================================================
-- SECTION 1 — REFERENCE AND CONFIGURATION
-- No dependencies. Created first because everything else references them.
-- =============================================================================

CREATE TABLE units_of_measure (
    unit_code           TEXT    PRIMARY KEY,
    name_ar             TEXT    NOT NULL,
    name_fr             TEXT    NOT NULL,
    dimension           TEXT    NOT NULL
        CHECK (dimension IN ('count','weight','volume','length')),
    base_unit_code      TEXT    REFERENCES units_of_measure(unit_code),
    factor_to_base      INTEGER NOT NULL DEFAULT 1000000
        CHECK (factor_to_base > 0),     -- scaled 1e6; g->kg = 1000
    decimal_places      INTEGER NOT NULL DEFAULT 0
        CHECK (decimal_places BETWEEN 0 AND 3),
    is_active           INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1)),
    created_at          TEXT    NOT NULL
) STRICT;

CREATE TABLE reason_codes (
    reason_code         TEXT    PRIMARY KEY,
    applies_to          TEXT    NOT NULL
        CHECK (applies_to IN ('discount','price_override','adjustment','void',
                                'return','no_sale','cash_movement','write_off')),
    label_ar            TEXT    NOT NULL,
    label_fr            TEXT    NOT NULL,
    requires_note       INTEGER NOT NULL DEFAULT 0 CHECK (requires_note IN (0,1)),
    requires_manager    INTEGER NOT NULL DEFAULT 0 CHECK (requires_manager IN (0,1)),
    display_order       INTEGER NOT NULL DEFAULT 0,
    is_active           INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1)),
    created_at          TEXT    NOT NULL
) STRICT;

-- The Article 32 notice text held as data, not as a string constant in code.
CREATE TABLE notice_versions (
    version_code        TEXT    PRIMARY KEY,
    notice_type         TEXT    NOT NULL
        CHECK (notice_type IN ('processing','marketing','staff')),
    language            TEXT    NOT NULL CHECK (language IN ('ar','fr','en')),
    body_text           TEXT    NOT NULL,
    effective_from      TEXT    NOT NULL,
    effective_to        TEXT,
    published_at        TEXT    NOT NULL,
    CHECK (effective_to IS NULL OR effective_to > effective_from)
) STRICT;

-- Jurisdiction stays a configuration parameter. This is where that lives.
CREATE TABLE retention_policies (
    policy_code             TEXT    PRIMARY KEY,
    entity_type             TEXT    NOT NULL
        CHECK (entity_type IN ('transaction','customer','staff','processing_log',
                                'consent_event','recommendation','stock_movement')),
    retention_days          INTEGER NOT NULL CHECK (retention_days > 0),
    legal_basis_reference   TEXT    NOT NULL,
    action_on_expiry        TEXT    NOT NULL
        CHECK (action_on_expiry IN ('delete','unlink','archive')),
    is_active               INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1)),
    created_at              TEXT    NOT NULL,
    updated_at              TEXT    NOT NULL
) STRICT;

-- Retailer-set values. Distinct from parameter_registry, which is engine-computed.
CREATE TABLE system_config (
    config_key          TEXT    PRIMARY KEY,
    config_value        TEXT    NOT NULL,
    data_type           TEXT    NOT NULL
        CHECK (data_type IN ('text','integer','money','percent','bool','date')),
    description         TEXT,
    updated_by          TEXT,
    updated_at          TEXT    NOT NULL
) STRICT;

INSERT INTO system_config (config_key, config_value, data_type, description, updated_at) VALUES
    ('embedded_barcode_prefixes','20,21,22,23,24,25,26,27,28,29','text',
    'EAN-13 prefixes reserved for in-store use, including weight-embedded labels', datetime('now')),
    ('embedded_barcode_item_digits','2-7','text','Positions holding the item code', datetime('now')),
    ('embedded_barcode_value_digits','8-12','text','Positions holding weight or price', datetime('now'));

-- Cloud-authoritative, arrives on control-plane channel E.
-- The store must know its own tier offline: the Customer department is gated.
CREATE TABLE store_entitlements (
    entitlement_code    TEXT    PRIMARY KEY,
    is_enabled          INTEGER NOT NULL DEFAULT 0 CHECK (is_enabled IN (0,1)),
    tier                TEXT    CHECK (tier IN ('basic','pro','enterprise')),
    valid_from          TEXT,
    valid_to            TEXT,
    synced_at           TEXT    NOT NULL
) STRICT;

-- =============================================================================
-- SECTION 2 — ORGANISATION
-- stores <-> staff is a circular reference (manager_id / store_id).
-- SQLite resolves foreign keys at DML time, so the forward reference is legal.
-- Insert a store with manager_id NULL first, then the staff, then UPDATE.
-- =============================================================================

CREATE TABLE stores (
    store_id                TEXT    PRIMARY KEY,
    store_code              TEXT    NOT NULL UNIQUE,
    store_name              TEXT    NOT NULL,
    contact_phone           TEXT,
    email                   TEXT,
    address                 TEXT,
    latitude                TEXT,           -- decimal degrees, as text;
    longitude               TEXT,
    store_type              TEXT    NOT NULL,   -- seeds attributes and reason codes only
    currency                TEXT    NOT NULL DEFAULT 'DZD',
    timezone                TEXT    NOT NULL DEFAULT 'Africa/Algiers',
    tax_registration_number TEXT,               -- NIF / NIS
    fiscal_year_start       TEXT    NOT NULL DEFAULT '01-01',   -- 'MM-DD'
    manager_id              TEXT    REFERENCES staff(staff_id),
    status                  TEXT    NOT NULL DEFAULT 'active'
        CHECK (status IN ('active','suspended','closed')),
    created_at              TEXT    NOT NULL,
    updated_at              TEXT    NOT NULL
) STRICT;

CREATE TABLE staff (
    staff_id                        TEXT    PRIMARY KEY,
    store_id                        TEXT    NOT NULL REFERENCES stores(store_id),
    staff_name                      TEXT    NOT NULL,
    contact_phone                   TEXT,
    email                           TEXT,
    role                            TEXT    NOT NULL REFERENCES roles(role_code),
    pin_hash                        TEXT    NOT NULL,   -- never the PIN itself
    join_date                       TEXT    NOT NULL,
    last_seen_date                  TEXT,
    termination_date                TEXT,
    notice_version_acknowledged     TEXT    REFERENCES notice_versions(version_code),
    notice_acknowledged_at          TEXT,
    status                          TEXT    NOT NULL DEFAULT 'active'
        CHECK (status IN ('active','suspended','terminated')),
    created_at                      TEXT    NOT NULL,
    updated_at                      TEXT    NOT NULL
) STRICT;

CREATE TABLE terminals (
    terminal_id         TEXT    PRIMARY KEY,
    store_id            TEXT    NOT NULL REFERENCES stores(store_id),
    terminal_name       TEXT    NOT NULL,
    is_replica          INTEGER NOT NULL DEFAULT 0 CHECK (is_replica IN (0,1)),
    printer_port        TEXT,
    hardware_notes      TEXT,
    last_seen_at        TEXT,
    status              TEXT    NOT NULL DEFAULT 'active'
        CHECK (status IN ('active','inactive','retired')),
    created_at          TEXT    NOT NULL,
    updated_at          TEXT    NOT NULL
) STRICT;

-- A staff work period. Distinct from cash_sessions, which is a drawer period.
CREATE TABLE shifts (
    shift_id            TEXT    PRIMARY KEY,
    staff_id            TEXT    NOT NULL REFERENCES staff(staff_id),
    terminal_id         TEXT    REFERENCES terminals(terminal_id),
    store_id            TEXT    NOT NULL REFERENCES stores(store_id),
    start_time          TEXT    NOT NULL,
    end_time            TEXT,
    status              TEXT    NOT NULL DEFAULT 'open'
        CHECK (status IN ('open','closed')),
    created_at          TEXT    NOT NULL,
    updated_at          TEXT    NOT NULL,
    CHECK (end_time IS NULL OR end_time >= start_time)
) STRICT;

-- A drawer period. One cashier may span two sessions; one session may span
-- two cashiers across a handover. Hence separate from shifts.
CREATE TABLE cash_sessions (
    session_id          TEXT    PRIMARY KEY,
    store_id            TEXT    NOT NULL REFERENCES stores(store_id),
    terminal_id         TEXT    NOT NULL REFERENCES terminals(terminal_id),
    opened_by           TEXT    NOT NULL REFERENCES staff(staff_id),
    opened_at           TEXT    NOT NULL,
    opening_float       INTEGER NOT NULL DEFAULT 0,     -- centimes
    closed_by           TEXT    REFERENCES staff(staff_id),
    closed_at           TEXT,
    counted_cash        INTEGER,                        -- centimes
    expected_cash       INTEGER,                        -- centimes
    variance            INTEGER,                        -- counted - expected
    z_report_number     INTEGER,                        -- sequential per store
    status              TEXT    NOT NULL DEFAULT 'open'
        CHECK (status IN ('open','closed','suspended')),
    notes               TEXT,
    created_at          TEXT    NOT NULL,
    updated_at          TEXT    NOT NULL,
    CHECK (closed_at IS NULL OR closed_at >= opened_at),
    CHECK (status <> 'closed' OR counted_cash IS NOT NULL)
) STRICT;

CREATE TABLE cash_movements (
    movement_id         TEXT    PRIMARY KEY,
    session_id          TEXT    NOT NULL REFERENCES cash_sessions(session_id),
    movement_type       TEXT    NOT NULL
        CHECK (movement_type IN ('paid_in','paid_out','drop','float_add','float_remove')),
    amount              INTEGER NOT NULL CHECK (amount > 0),    -- centimes, always positive
    reason_code         TEXT    NOT NULL REFERENCES reason_codes(reason_code),
    note                TEXT,
    staff_id            TEXT    NOT NULL REFERENCES staff(staff_id),
    authorised_by       TEXT    REFERENCES staff(staff_id),
    occurred_at         TEXT    NOT NULL
) STRICT;

-- =============================================================================
-- SECTION 3 — CATALOGUE
-- =============================================================================

CREATE TABLE categories (
    category_id         TEXT    PRIMARY KEY,
    category_name       TEXT    NOT NULL,
    parent_id           TEXT    REFERENCES categories(category_id),
    slug                TEXT    NOT NULL UNIQUE,
    description         TEXT,
    image               TEXT,
    tax_rate            INTEGER,        -- basis points; NULL falls back to system_config
    sensitive_flag      INTEGER NOT NULL DEFAULT 0 CHECK (sensitive_flag IN (0,1)),
    sensitive_reason    TEXT,
    status              TEXT    NOT NULL DEFAULT 'active'
        CHECK (status IN ('active','archived')),
    created_at          TEXT    NOT NULL,
    updated_at          TEXT    NOT NULL,
    CHECK (tax_rate IS NULL OR tax_rate BETWEEN 0 AND 10000),
    CHECK (sensitive_flag = 0 OR sensitive_reason IS NOT NULL),
    CHECK (parent_id IS NULL OR parent_id <> category_id)
) STRICT;

CREATE TABLE products (
    product_id          TEXT    PRIMARY KEY,
    product_name        TEXT    NOT NULL,
    description         TEXT,
    status              TEXT    NOT NULL DEFAULT 'active'
        CHECK (status IN ('active','discontinued','archived')),
    created_at          TEXT    NOT NULL,
    updated_at          TEXT    NOT NULL
    -- company/designer, country_of_origin, material_composition, gender_category,
    -- use_instructions, seasonality, images, box_quantity: all moved to attributes
) STRICT;

CREATE TABLE variants (
    variant_id          TEXT    PRIMARY KEY,
    product_id          TEXT    NOT NULL REFERENCES products(product_id),
    variant_name        TEXT    NOT NULL,
    barcode             TEXT    UNIQUE,     -- NULLs are distinct in SQLite UNIQUE
    plu                 TEXT    UNIQUE,     -- short code for weighted goods
    sku                 TEXT    UNIQUE,
    barcode_type        TEXT    NOT NULL DEFAULT 'standard' CHECK (barcode_type IN ('standard', 'weight_embedded', 'price_embedded')),
    selling_unit_code   TEXT    NOT NULL REFERENCES units_of_measure(unit_code),
    is_weighted         INTEGER NOT NULL DEFAULT 0 CHECK (is_weighted IN (0,1)),
    tare_weight         INTEGER NOT NULL DEFAULT 0,     -- thousandths, subtracted from scale
    image               TEXT,
    status              TEXT    NOT NULL DEFAULT 'active'
        CHECK (status IN ('active','discontinued','archived')),
    created_at          TEXT    NOT NULL,
    updated_at          TEXT    NOT NULL,
    CHECK (tare_weight >= 0),
    CHECK (is_weighted = 0 OR plu IS NOT NULL OR barcode IS NOT NULL)
    -- size, weight, volume, color, pattern: all moved to attributes
) STRICT;

CREATE TABLE product_category (
    product_id          TEXT    NOT NULL REFERENCES products(product_id) ON DELETE CASCADE,
    category_id         TEXT    NOT NULL REFERENCES categories(category_id),
    is_primary          INTEGER NOT NULL DEFAULT 0 CHECK (is_primary IN (0,1)),
    added_at            TEXT    NOT NULL,
    PRIMARY KEY (product_id, category_id)
) STRICT;

-- Exactly one primary category per product.
CREATE UNIQUE INDEX ux_product_category_primary
    ON product_category(product_id) WHERE is_primary = 1;

-- -----------------------------------------------------------------------------
-- Attribute model (EAV). Replaces the hardcoded, apparel-shaped columns.
-- Store type becomes a seed of definitions at onboarding, never a code branch.
-- -----------------------------------------------------------------------------

CREATE TABLE attribute_definitions (
    attribute_code      TEXT    PRIMARY KEY,    -- 'country_of_origin', 'dosage_mg'
    label_ar            TEXT    NOT NULL,
    label_fr            TEXT    NOT NULL,
    label_en            TEXT,
    data_type           TEXT    NOT NULL
        CHECK (data_type IN ('text','number','bool','date','enum')),
    unit_code           TEXT    REFERENCES units_of_measure(unit_code),
    applies_to          TEXT    NOT NULL CHECK (applies_to IN ('product','variant')),
    is_groupable        INTEGER NOT NULL DEFAULT 0 CHECK (is_groupable IN (0,1)),
    is_filterable       INTEGER NOT NULL DEFAULT 0 CHECK (is_filterable IN (0,1)),
    help_text           TEXT,
    status              TEXT    NOT NULL DEFAULT 'active'
        CHECK (status IN ('active','archived')),
    created_at          TEXT    NOT NULL,
    updated_at          TEXT    NOT NULL,
    -- a unit only makes sense on a number
    CHECK (unit_code IS NULL OR data_type = 'number')
) STRICT;

-- Controlled vocabulary for enum attributes.
CREATE TABLE attribute_options (
    attribute_code      TEXT    NOT NULL REFERENCES attribute_definitions(attribute_code) ON DELETE CASCADE,
    option_code         TEXT    NOT NULL,
    label_ar            TEXT    NOT NULL,
    label_fr            TEXT    NOT NULL,
    display_order       INTEGER NOT NULL DEFAULT 0,
    is_active           INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1)),
    created_at          TEXT    NOT NULL,
    PRIMARY KEY (attribute_code, option_code)
) STRICT;

CREATE TABLE product_attribute_values (
    product_id          TEXT    NOT NULL REFERENCES products(product_id) ON DELETE CASCADE,
    attribute_code      TEXT    NOT NULL REFERENCES attribute_definitions(attribute_code),
    value_text          TEXT,
    value_number        INTEGER,        -- scaled per the attribute's unit
    value_bool          INTEGER CHECK (value_bool IS NULL OR value_bool IN (0,1)),
    value_date          TEXT,
    option_code         TEXT,
    updated_at          TEXT    NOT NULL,
    PRIMARY KEY (product_id, attribute_code),
    FOREIGN KEY (attribute_code, option_code)
        REFERENCES attribute_options(attribute_code, option_code),
    -- exactly one value column populated
    CHECK (
        (CASE WHEN value_text   IS NOT NULL THEN 1 ELSE 0 END) +
        (CASE WHEN value_number IS NOT NULL THEN 1 ELSE 0 END) +
        (CASE WHEN value_bool   IS NOT NULL THEN 1 ELSE 0 END) +
        (CASE WHEN value_date   IS NOT NULL THEN 1 ELSE 0 END) +
        (CASE WHEN option_code  IS NOT NULL THEN 1 ELSE 0 END) = 1
    )
) STRICT;

CREATE TABLE variant_attribute_values (
    variant_id          TEXT    NOT NULL REFERENCES variants(variant_id) ON DELETE CASCADE,
    attribute_code      TEXT    NOT NULL REFERENCES attribute_definitions(attribute_code),
    value_text          TEXT,
    value_number        INTEGER,
    value_bool          INTEGER CHECK (value_bool IS NULL OR value_bool IN (0,1)),
    value_date          TEXT,
    option_code         TEXT,
    updated_at          TEXT    NOT NULL,
    PRIMARY KEY (variant_id, attribute_code),
    FOREIGN KEY (attribute_code, option_code)
        REFERENCES attribute_options(attribute_code, option_code),
    CHECK (
        (CASE WHEN value_text   IS NOT NULL THEN 1 ELSE 0 END) +
        (CASE WHEN value_number IS NOT NULL THEN 1 ELSE 0 END) +
        (CASE WHEN value_bool   IS NOT NULL THEN 1 ELSE 0 END) +
        (CASE WHEN value_date   IS NOT NULL THEN 1 ELSE 0 END) +
        (CASE WHEN option_code  IS NOT NULL THEN 1 ELSE 0 END) = 1
    )
) STRICT;

CREATE TABLE category_attributes (
    category_id             TEXT    NOT NULL REFERENCES categories(category_id) ON DELETE CASCADE,
    attribute_code          TEXT    NOT NULL REFERENCES attribute_definitions(attribute_code) ON DELETE CASCADE,
    is_required             INTEGER NOT NULL DEFAULT 0 CHECK (is_required IN (0,1)),
    display_order           INTEGER NOT NULL DEFAULT 0,
    inherits_to_children    INTEGER NOT NULL DEFAULT 1 CHECK (inherits_to_children IN (0,1)),
    PRIMARY KEY (category_id, attribute_code)
) STRICT;

-- =============================================================================
-- SECTION 4 — PRICING AND PROMOTIONS
-- =============================================================================

CREATE TABLE prices (
    variant_id          TEXT    NOT NULL REFERENCES variants(variant_id),
    valid_from          TEXT    NOT NULL,
    store_id            TEXT    NOT NULL REFERENCES stores(store_id),
    price_type          TEXT    NOT NULL DEFAULT 'retail'
        CHECK (price_type IN ('retail','wholesale','staff','promotional')),
    price               INTEGER NOT NULL CHECK (price >= 0),    -- centimes
    currency            TEXT    NOT NULL DEFAULT 'DZD',
    is_tax_inclusive    INTEGER NOT NULL DEFAULT 1 CHECK (is_tax_inclusive IN (0,1)),
    valid_to            TEXT,
    created_by          TEXT    REFERENCES staff(staff_id),
    created_at          TEXT    NOT NULL,
    PRIMARY KEY (variant_id, valid_from, store_id, price_type),
    CHECK (valid_to IS NULL OR valid_to > valid_from)
) STRICT;

CREATE TABLE promotions (
    promotion_id        TEXT    PRIMARY KEY,
    promotion_name      TEXT    NOT NULL,
    promotion_type      TEXT    NOT NULL
        CHECK (promotion_type IN ('discount','bogo','bundle','markdown')),
    store_id            TEXT    REFERENCES stores(store_id),   -- NULL = all stores
    source_recommendation_id TEXT REFERENCES recommendations(recommendation_id),      -- set when the engine proposed it
    status              TEXT    NOT NULL DEFAULT 'draft'
        CHECK (status IN ('draft','scheduled','active','ended','cancelled')),
    created_by          TEXT    REFERENCES staff(staff_id),
    created_at          TEXT    NOT NULL,
    updated_at          TEXT    NOT NULL
) STRICT;

-- priority and is_stackable are required: without them, two overlapping
-- promotions have undefined behaviour at the till.
CREATE TABLE promotion_variant (
    promotion_id        TEXT    NOT NULL REFERENCES promotions(promotion_id) ON DELETE CASCADE,
    variant_id          TEXT    NOT NULL REFERENCES variants(variant_id),
    valid_from          TEXT    NOT NULL,
    valid_to            TEXT,
    value_type          TEXT    NOT NULL CHECK (value_type IN ('percent','amount','bogo')),
    promotion_value     INTEGER NOT NULL CHECK (promotion_value >= 0),
    min_quantity        INTEGER NOT NULL DEFAULT 0,     -- thousandths
    max_redemptions     INTEGER,
    redemption_count    INTEGER NOT NULL DEFAULT 0,
    priority            INTEGER NOT NULL DEFAULT 100,   -- lower wins
    is_stackable        INTEGER NOT NULL DEFAULT 0 CHECK (is_stackable IN (0,1)),
    description         TEXT,
    status              TEXT    NOT NULL DEFAULT 'active'
        CHECK (status IN ('active','paused','ended')),
    PRIMARY KEY (promotion_id, variant_id, valid_from),
    CHECK (valid_to IS NULL OR valid_to > valid_from),
    CHECK (value_type <> 'percent' OR promotion_value <= 10000)
) STRICT;

CREATE TABLE promotion_product (
    promotion_id        TEXT    NOT NULL REFERENCES promotions(promotion_id) ON DELETE CASCADE,
    product_id          TEXT    NOT NULL REFERENCES products(product_id),
    valid_from          TEXT    NOT NULL,
    valid_to            TEXT,
    value_type          TEXT    NOT NULL CHECK (value_type IN ('percent','amount','bogo')),
    promotion_value     INTEGER NOT NULL CHECK (promotion_value >= 0),
    min_quantity        INTEGER NOT NULL DEFAULT 0,
    max_redemptions     INTEGER,
    redemption_count    INTEGER NOT NULL DEFAULT 0,
    priority            INTEGER NOT NULL DEFAULT 100,
    is_stackable        INTEGER NOT NULL DEFAULT 0 CHECK (is_stackable IN (0,1)),
    description         TEXT,
    status              TEXT    NOT NULL DEFAULT 'active'
        CHECK (status IN ('active','paused','ended')),
    PRIMARY KEY (promotion_id, product_id, valid_from),
    CHECK (valid_to IS NULL OR valid_to > valid_from),
    CHECK (value_type <> 'percent' OR promotion_value <= 10000)
) STRICT;


CREATE TABLE product_bundles (
    bundle_id           TEXT    PRIMARY KEY,
    barcode             TEXT    UNIQUE,
    bundle_name         TEXT    NOT NULL,
    bundle_type         TEXT    NOT NULL CHECK (bundle_type IN ('fixed','mix_and_match')),
    status              TEXT    NOT NULL DEFAULT 'active'
        CHECK (status IN ('active','inactive','archived')),
    created_at          TEXT    NOT NULL,
    updated_at          TEXT    NOT NULL
) STRICT;

CREATE TABLE product_bundle_items (
    bundle_item_id      TEXT    PRIMARY KEY,
    bundle_id           TEXT    NOT NULL REFERENCES product_bundles(bundle_id) ON DELETE CASCADE,
    variant_id          TEXT    NOT NULL REFERENCES variants(variant_id),
    quantity            INTEGER NOT NULL CHECK (quantity > 0),  -- thousandths
    valid_from          TEXT    NOT NULL,
    valid_to            TEXT,
    description         TEXT,
    status              TEXT    NOT NULL DEFAULT 'active'
        CHECK (status IN ('active','inactive')),
    CHECK (valid_to IS NULL OR valid_to > valid_from)
) STRICT;

-- =============================================================================
-- SECTION 5 — SUPPLIERS AND PURCHASING
-- =============================================================================

CREATE TABLE suppliers (
    supplier_id         TEXT    PRIMARY KEY,
    supplier_code       TEXT    NOT NULL UNIQUE,
    company_name        TEXT    NOT NULL,
    responsible_name    TEXT,
    contact_phone       TEXT,
    email               TEXT,
    website             TEXT,
    shipping_address    TEXT,
    net_days            INTEGER NOT NULL DEFAULT 0 CHECK (net_days >= 0),
    credit_limit        INTEGER,        -- centimes
    currency            TEXT    NOT NULL DEFAULT 'DZD',
    status              TEXT    NOT NULL DEFAULT 'active'
        CHECK (status IN ('active','inactive','blacklisted')),
    created_at          TEXT    NOT NULL,
    updated_at          TEXT    NOT NULL
) STRICT;

CREATE TABLE supplier_variant (
    supplier_id             TEXT    NOT NULL REFERENCES suppliers(supplier_id),
    variant_id              TEXT    NOT NULL REFERENCES variants(variant_id),
    purchase_unit_code      TEXT    NOT NULL REFERENCES units_of_measure(unit_code),
    units_per_purchase_unit INTEGER NOT NULL DEFAULT 1000 CHECK (units_per_purchase_unit > 0),
    minimum_order_quantity  INTEGER NOT NULL DEFAULT 0,     -- in purchase units, thousandths
    stated_lead_time_days          INTEGER CHECK (stated_lead_time_days IS NULL OR stated_lead_time_days >= 0),
    purchase_price          INTEGER NOT NULL CHECK (purchase_price >= 0),  -- centimes per purchase unit
    currency                TEXT    NOT NULL DEFAULT 'DZD',
    net_days                INTEGER,
    last_price_at           TEXT,
    is_active               INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1)),
    created_at              TEXT    NOT NULL,
    updated_at              TEXT    NOT NULL,
    PRIMARY KEY (supplier_id, variant_id)
) STRICT;

CREATE TABLE purchase_orders (
    order_id                    TEXT    PRIMARY KEY,
    store_id                    TEXT    NOT NULL REFERENCES stores(store_id),
    supplier_id                 TEXT    NOT NULL REFERENCES suppliers(supplier_id),
    order_date                  TEXT    NOT NULL,
    expected_arrival_date       TEXT,
    received_at                 TEXT,
    total_amount                INTEGER,        -- centimes
    currency                    TEXT    NOT NULL DEFAULT 'DZD',
    source                      TEXT    NOT NULL DEFAULT 'manual'
        CHECK (source IN ('manual','recommendation','reorder_rule')),
    source_recommendation_id TEXT REFERENCES recommendations(recommendation_id),      -- closes the revealed-preference loop
    created_by                  TEXT    REFERENCES staff(staff_id),
    status                      TEXT    NOT NULL DEFAULT 'draft'
        CHECK (status IN ('draft','sent','partially_received','received','cancelled')),
    notes                       TEXT,
    created_at                  TEXT    NOT NULL,
    updated_at                  TEXT    NOT NULL
) STRICT;

CREATE TABLE purchase_order_items (
    order_id                    TEXT    NOT NULL REFERENCES purchase_orders(order_id) ON DELETE CASCADE,
    variant_id                  TEXT    NOT NULL REFERENCES variants(variant_id),
    quantity_ordered            INTEGER NOT NULL CHECK (quantity_ordered > 0),  -- thousandths
    quantity_received           INTEGER NOT NULL DEFAULT 0 CHECK (quantity_received >= 0),
    purchase_unit_code          TEXT    NOT NULL REFERENCES units_of_measure(unit_code),
    units_per_purchase_unit     INTEGER NOT NULL DEFAULT 1000,
    unit_cost                   INTEGER NOT NULL CHECK (unit_cost >= 0),        -- centimes
    discount                    INTEGER NOT NULL DEFAULT 0 CHECK (discount >= 0),
    line_total                  INTEGER NOT NULL CHECK (line_total >= 0),
    PRIMARY KEY (order_id, variant_id)
) STRICT;

-- =============================================================================
-- SECTION 6 — INVENTORY
-- A batch is a delivered lot and may contain several variants.
-- One expiry date per batch: if a delivery has differing expiry, that is two
-- batches. Cost therefore lives on batch_items, not on batches.
-- =============================================================================

CREATE TABLE batches (
    batch_id                TEXT    PRIMARY KEY,
    lot_number              TEXT,
    supplier_document_ref   TEXT,
    product_id              TEXT    NOT NULL REFERENCES products(product_id),
    store_id                TEXT    NOT NULL REFERENCES stores(store_id),
    supplier_id             TEXT    REFERENCES suppliers(supplier_id),
    order_id                TEXT    REFERENCES purchase_orders(order_id),
    received_date           TEXT    NOT NULL,
    expiration_date         TEXT,               -- NULL for non-perishables
    received_by             TEXT    REFERENCES staff(staff_id),
    status                  TEXT    NOT NULL DEFAULT 'active'
        CHECK (status IN ('active','depleted','written_off','quarantined')),
    created_at          TEXT    NOT NULL,
    CHECK (expiration_date IS NULL OR expiration_date >= received_date)
) STRICT;

-- The FIFO cost basis. quantity_received is the immutable receipt fact that
-- inventories (current stock) cannot provide, and is what shrinkage is measured
-- against.
CREATE TABLE batch_items (
    batch_id            TEXT    NOT NULL REFERENCES batches(batch_id),
    variant_id          TEXT    NOT NULL REFERENCES variants(variant_id),
    quantity_received   INTEGER NOT NULL CHECK (quantity_received > 0),  -- thousandths
    unit_code           TEXT    NOT NULL REFERENCES units_of_measure(unit_code),
    unit_cost           INTEGER NOT NULL CHECK (unit_cost >= 0),         -- centimes
    currency            TEXT    NOT NULL DEFAULT 'DZD',
    created_at          TEXT    NOT NULL,
    PRIMARY KEY (batch_id, variant_id)
) STRICT;

-- Current on-hand stock. Quantity may go negative: by Operating Rules §4 says
-- Reconciliation surfaces it.
CREATE TABLE inventories (
    store_id            TEXT    NOT NULL REFERENCES stores(store_id),
    variant_id          TEXT    NOT NULL REFERENCES variants(variant_id),
    batch_id            TEXT    NOT NULL REFERENCES batches(batch_id),
    quantity            INTEGER NOT NULL DEFAULT 0,     -- thousandths, may be negative
    reserved_quantity   INTEGER NOT NULL DEFAULT 0 CHECK (reserved_quantity >= 0),
    updated_at          TEXT    NOT NULL,
    PRIMARY KEY (store_id, variant_id, batch_id)
) STRICT;

-- The single stock audit trail. Append-only, enforced by trigger below.
-- variant_id is REQUIRED: a batch now holds several variants, so batch_id
-- alone is ambiguous.
CREATE TABLE stock_movements (
    movement_id         TEXT    PRIMARY KEY,
    store_id            TEXT    NOT NULL REFERENCES stores(store_id),
    variant_id          TEXT    NOT NULL REFERENCES variants(variant_id),
    batch_id            TEXT    NOT NULL REFERENCES batches(batch_id),
    movement_date       TEXT    NOT NULL,
    movement_type       TEXT    NOT NULL
        CHECK (movement_type IN ('receipt','sale','return_in','return_out',
                                'adjustment','count','write_off','expiry','transfer')),
    quantity_changed    INTEGER NOT NULL,               -- thousandths, signed
    unit_code           TEXT    NOT NULL REFERENCES units_of_measure(unit_code),
    unit_cost           INTEGER,                        -- centimes; write-offs carry value
    reference_type      TEXT    CHECK (reference_type IS NULL OR reference_type IN
                                    ('transaction','purchase_order','stock_count','return','manual')),
    reference_id        TEXT,
    reason_code         TEXT    REFERENCES reason_codes(reason_code),
    staff_id            TEXT    REFERENCES staff(staff_id),
    note                TEXT,
    created_at          TEXT    NOT NULL,
    CHECK (quantity_changed <> 0)
) STRICT;

CREATE TABLE stock_counts (
    count_id                TEXT    PRIMARY KEY,
    store_id                TEXT    NOT NULL REFERENCES stores(store_id),
    count_type              TEXT    NOT NULL CHECK (count_type IN ('full','cycle','spot')),
    scope_category_id       TEXT    REFERENCES categories(category_id),
    started_by              TEXT    NOT NULL REFERENCES staff(staff_id),
    started_at              TEXT    NOT NULL,
    completed_at            TEXT,
    approved_by             TEXT    REFERENCES staff(staff_id),
    approved_at             TEXT,
    total_variance_value    INTEGER,        -- centimes, signed
    status                  TEXT    NOT NULL DEFAULT 'draft'
        CHECK (status IN ('draft','counting','review','posted','cancelled')),
    notes                   TEXT,
    created_at              TEXT    NOT NULL,
    updated_at              TEXT    NOT NULL,
    CHECK (status <> 'posted' OR approved_by IS NOT NULL)
) STRICT;

CREATE TABLE stock_count_items (
    count_item_id       TEXT    PRIMARY KEY,
    count_id            TEXT    NOT NULL REFERENCES stock_counts(count_id) ON DELETE CASCADE,
    variant_id          TEXT    NOT NULL REFERENCES variants(variant_id),
    batch_id            TEXT    NOT NULL REFERENCES batches(batch_id),
    expected_quantity   INTEGER NOT NULL,               -- snapshot at count start
    counted_quantity    INTEGER,
    variance_quantity   INTEGER,
    unit_cost           INTEGER,                        -- centimes, for valuing variance
    variance_value      INTEGER,
    counted_by          TEXT    REFERENCES staff(staff_id),
    counted_at          TEXT,
    recount_flag        INTEGER NOT NULL DEFAULT 0 CHECK (recount_flag IN (0,1)),
    note                TEXT,
    UNIQUE (count_id, variant_id, batch_id)
) STRICT;

-- =============================================================================
-- SECTION 7 — CUSTOMERS AND COMPLIANCE
-- Customers are tenant-wide, not store-scoped: they belong to all stores.
-- =============================================================================

CREATE TABLE customers (
    customer_id                     TEXT    PRIMARY KEY,
    customer_name                   TEXT    NOT NULL,
    contact_phone                   TEXT,
    email                           TEXT,
    preferred_language              TEXT    NOT NULL DEFAULT 'ar'
        CHECK (preferred_language IN ('ar','fr','en')),
    join_date                       TEXT    NOT NULL,
    last_order_date                 TEXT,
    points                          INTEGER NOT NULL DEFAULT 0,   -- cached; ledger is truth
    credit                          INTEGER NOT NULL DEFAULT 0,   -- centimes; ledger is truth
    discount                        INTEGER NOT NULL DEFAULT 0,   -- basis points
    tier_ranking                    TEXT, -- we'll later define values and add a check in constraint
    ecommerce_flag                  INTEGER NOT NULL DEFAULT 0 CHECK (ecommerce_flag IN (0,1)),
    legal_basis                     TEXT    NOT NULL DEFAULT 'contract'
        CHECK (legal_basis IN ('consent','contract','legal_obligation','legitimate_interest')),
    consent_profiling               INTEGER NOT NULL DEFAULT 0 CHECK (consent_profiling IN (0,1)),
    consent_profiling_at            TEXT,
    consent_profiling_notice_version TEXT   REFERENCES notice_versions(version_code),
    consent_marketing               INTEGER NOT NULL DEFAULT 0 CHECK (consent_marketing IN (0,1)),
    consent_marketing_at            TEXT,
    consent_marketing_notice_version TEXT   REFERENCES notice_versions(version_code),
    objection_flag                  INTEGER NOT NULL DEFAULT 0 CHECK (objection_flag IN (0,1)),
    deletion_requested_at           TEXT,
    status                          TEXT    NOT NULL DEFAULT 'active'
        CHECK (status IN ('active','inactive','erased')),
    created_at                      TEXT    NOT NULL,
    updated_at                      TEXT    NOT NULL,
    CHECK (consent_profiling = 0 OR consent_profiling_at IS NOT NULL),
    CHECK (consent_marketing = 0 OR consent_marketing_at IS NOT NULL),
    CHECK (discount BETWEEN 0 AND 10000)
) STRICT;

-- Append-only. Withdrawal is a new event, never an update.
CREATE TABLE consent_events (
    consent_event_id    TEXT    PRIMARY KEY,
    customer_id         TEXT    NOT NULL REFERENCES customers(customer_id),
    occurred_at         TEXT    NOT NULL,
    action              TEXT    NOT NULL CHECK (action IN ('granted','withdrawn','renewed')),
    consent_type        TEXT    NOT NULL CHECK (consent_type IN ('processing','marketing')),
    notice_version      TEXT    NOT NULL REFERENCES notice_versions(version_code),
    captured_by         TEXT    REFERENCES staff(staff_id),
    method              TEXT    NOT NULL CHECK (method IN ('verbal','written','digital')),
    terminal_id         TEXT    REFERENCES terminals(terminal_id)
) STRICT;

CREATE TABLE data_subject_requests (
    request_id          TEXT    PRIMARY KEY,
    customer_id         TEXT    NOT NULL REFERENCES customers(customer_id),
    request_type        TEXT    NOT NULL
        CHECK (request_type IN ('information','access','rectification','objection','erasure')),
    received_at         TEXT    NOT NULL,
    due_at              TEXT    NOT NULL,      -- Article 35: ten days for rectification
    received_by         TEXT    REFERENCES staff(staff_id),
    status              TEXT    NOT NULL DEFAULT 'open'
        CHECK (status IN ('open','in_progress','fulfilled','refused','blocked')),
    resolution_note     TEXT,
    resolved_at         TEXT,
    created_at          TEXT    NOT NULL,
    updated_at          TEXT    NOT NULL,
    CHECK (status NOT IN ('refused','blocked') OR resolution_note IS NOT NULL)
) STRICT;

-- Written at EVERY access site. Article 41 bis 3.
-- Not trigger-protected against UPDATE: erasure purges its identity columns.
CREATE TABLE processing_log (
    log_id              TEXT    PRIMARY KEY,
    occurred_at         TEXT    NOT NULL,
    operation           TEXT    NOT NULL
        CHECK (operation IN ('collection','consultation','disclosure','transmission',
                            'modification','erasure','pseudonymisation','re_identification')),
    subject_type        TEXT    NOT NULL CHECK (subject_type IN ('customer','staff')),
    subject_id          TEXT,               -- nullable: purged on erasure
    actor_type          TEXT    NOT NULL CHECK (actor_type IN ('staff','system','engine')),
    actor_id            TEXT,
    purpose             TEXT    NOT NULL,
    recipient           TEXT,
    source_module       TEXT    NOT NULL,
    store_id            TEXT    REFERENCES stores(store_id),
    terminal_id         TEXT    REFERENCES terminals(terminal_id)
) STRICT;

-- Append-only, lives outside the erased data, re-applied on every restore.
-- After unlinking, the data itself can no longer prove what was erased:
-- this ledger IS the evidence of compliance.
CREATE TABLE erasure_ledger (
    erasure_id          TEXT    PRIMARY KEY,
    subject_type        TEXT    NOT NULL CHECK (subject_type IN ('customer','staff')),
    subject_id          TEXT    NOT NULL,   -- a bare ULID; identifies nothing once the row is gone
    request_id          TEXT    REFERENCES data_subject_requests(request_id),
    requested_at        TEXT    NOT NULL,
    executed_at         TEXT,
    executed_by         TEXT    REFERENCES staff(staff_id),
    scope_json          TEXT,               -- mapping deleted, cloud pseudonyms nulled, log purged
    status              TEXT    NOT NULL DEFAULT 'pending'
        CHECK (status IN ('pending','executed','blocked')),
    blocked_reason      TEXT,               -- fiscal retention, outstanding credit, open invoice
    cloud_confirmed_at  TEXT,
    created_at          TEXT    NOT NULL,
    CHECK (status <> 'blocked' OR blocked_reason IS NOT NULL),
    CHECK (status <> 'executed' OR executed_at IS NOT NULL)
) STRICT;

-- =============================================================================
-- SECTION 8 — SALES
-- =============================================================================

CREATE TABLE transactions (
    transaction_id          TEXT    PRIMARY KEY,
    store_id                TEXT    NOT NULL REFERENCES stores(store_id),
    terminal_id             TEXT    NOT NULL REFERENCES terminals(terminal_id),
    cash_session_id         TEXT    REFERENCES cash_sessions(session_id),
    staff_id                TEXT    NOT NULL REFERENCES staff(staff_id),
    customer_id             TEXT    REFERENCES customers(customer_id),
    invoice_number          TEXT,           -- gapless sequential per store per fiscal year
    occurred_at             TEXT    NOT NULL,
    subtotal                INTEGER NOT NULL DEFAULT 0,     -- centimes
    discount_total          INTEGER NOT NULL DEFAULT 0,
    tax_total               INTEGER NOT NULL DEFAULT 0,
    total_amount            INTEGER NOT NULL DEFAULT 0,
    currency                TEXT    NOT NULL DEFAULT 'DZD',
    ecommerce_flag          INTEGER NOT NULL DEFAULT 0 CHECK (ecommerce_flag IN (0,1)),
    original_transaction_id TEXT    REFERENCES transactions(transaction_id),
    status                  TEXT    NOT NULL DEFAULT 'open'
        CHECK (status IN ('open','parked','completed','voided','refunded','partially_refunded')),
    voided_at               TEXT,
    voided_by               TEXT    REFERENCES staff(staff_id),
    void_reason_code        TEXT    REFERENCES reason_codes(reason_code),
    created_at              TEXT    NOT NULL,
    updated_at              TEXT    NOT NULL,
    CHECK (status <> 'voided' OR (voided_at IS NOT NULL AND void_reason_code IS NOT NULL)),
    CHECK (status <> 'completed' OR invoice_number IS NOT NULL)

) STRICT;

CREATE UNIQUE INDEX ux_transactions_invoice
    ON transactions(store_id, invoice_number) WHERE invoice_number IS NOT NULL;

CREATE TABLE transaction_items (
    transaction_item_id     TEXT    PRIMARY KEY,
    transaction_id          TEXT    NOT NULL REFERENCES transactions(transaction_id),
    variant_id              TEXT    NOT NULL REFERENCES variants(variant_id),
    batch_id                TEXT    REFERENCES batches(batch_id),
    promotion_id            TEXT    REFERENCES promotions(promotion_id),
    quantity                INTEGER NOT NULL CHECK (quantity <> 0),  -- thousandths
    unit_code               TEXT    NOT NULL REFERENCES units_of_measure(unit_code),
    sell_price              INTEGER NOT NULL CHECK (sell_price >= 0), -- centimes
    unit_cost_at_sale       INTEGER,        -- FIFO snapshot; margin is unrecoverable without it
    discount_amount         INTEGER NOT NULL DEFAULT 0 CHECK (discount_amount >= 0),
    discount_reason_code    TEXT    REFERENCES reason_codes(reason_code),
    authorised_by           TEXT    REFERENCES staff(staff_id),   -- manager PIN on override
    tax_amount              INTEGER NOT NULL DEFAULT 0,
    line_total              INTEGER NOT NULL,
    created_at              TEXT    NOT NULL,
    CHECK (discount_amount = 0 OR discount_reason_code IS NOT NULL)
) STRICT;

CREATE TABLE transaction_payments (
    payment_id          TEXT    PRIMARY KEY,
    transaction_id      TEXT    NOT NULL REFERENCES transactions(transaction_id),
    sequence            INTEGER NOT NULL,
    payment_method      TEXT    NOT NULL
        CHECK (payment_method IN ('cash','card','mobile_wallet','store_credit','on_account')),
    amount              INTEGER NOT NULL CHECK (amount <> 0),    -- centimes; negative on refund
    currency            TEXT    NOT NULL DEFAULT 'DZD',
    reference           TEXT,               -- card auth code, wallet reference
    created_at          TEXT    NOT NULL,
    UNIQUE (transaction_id, sequence)
) STRICT;

CREATE TABLE returns (
    return_id               TEXT    PRIMARY KEY,
    transaction_item_id     TEXT    NOT NULL REFERENCES transaction_items(transaction_item_id),
    store_id                TEXT    NOT NULL REFERENCES stores(store_id),
    terminal_id             TEXT    REFERENCES terminals(terminal_id),
    batch_id                TEXT    REFERENCES batches(batch_id),
    staff_id                TEXT    NOT NULL REFERENCES staff(staff_id),
    approved_by             TEXT    REFERENCES staff(staff_id),
    quantity_returned       INTEGER NOT NULL CHECK (quantity_returned > 0),  -- thousandths
    refund_amount           INTEGER NOT NULL CHECK (refund_amount >= 0),     -- centimes
    refund_method           TEXT    NOT NULL
        CHECK (refund_method IN ('cash','card','store_credit','exchange')),
    restock_flag            INTEGER NOT NULL CHECK (restock_flag IN (0,1)),  -- sellable, or written off
    reason_code             TEXT    NOT NULL REFERENCES reason_codes(reason_code),
    note                    TEXT,
    created_at              TEXT    NOT NULL
) STRICT;

-- =============================================================================
-- SECTION 9 — LEDGERS
-- A bare balance cannot be reconciled after two offline terminals both
-- decrement it. These tables are the truth; the columns on customers are cache.
-- Append-only, enforced by trigger below.
-- =============================================================================

CREATE TABLE loyalty_movements (
    movement_id         TEXT    PRIMARY KEY,
    customer_id         TEXT    NOT NULL REFERENCES customers(customer_id),
    movement_type       TEXT    NOT NULL
        CHECK (movement_type IN ('earn','redeem','adjust','expire','reverse')),
    points              INTEGER NOT NULL CHECK (points <> 0),    -- signed
    balance_after       INTEGER NOT NULL,
    transaction_id      TEXT    REFERENCES transactions(transaction_id),
    staff_id            TEXT    REFERENCES staff(staff_id),
    terminal_id         TEXT    REFERENCES terminals(terminal_id),
    reason_code         TEXT    REFERENCES reason_codes(reason_code),
    note                TEXT,
    occurred_at         TEXT    NOT NULL
) STRICT;

CREATE TABLE credit_movements (
    movement_id         TEXT    PRIMARY KEY,
    customer_id         TEXT    NOT NULL REFERENCES customers(customer_id),
    movement_type       TEXT    NOT NULL
        CHECK (movement_type IN ('issue','redeem','adjust','expire','reverse')),
    amount              INTEGER NOT NULL CHECK (amount <> 0),    -- centimes, signed
    balance_after       INTEGER NOT NULL,
    transaction_id      TEXT    REFERENCES transactions(transaction_id),
    return_id           TEXT    REFERENCES returns(return_id),
    expires_at          TEXT,
    staff_id            TEXT    REFERENCES staff(staff_id),
    terminal_id         TEXT    REFERENCES terminals(terminal_id),
    reason_code         TEXT    REFERENCES reason_codes(reason_code),
    note                TEXT,
    occurred_at         TEXT    NOT NULL
) STRICT;

-- =============================================================================
-- SECTION 10 — ENGINE AND INTEGRATION LAYER
-- =============================================================================

-- The store evaluator reads ONLY from here. It compares; it never fits.
CREATE TABLE parameter_registry (
    parameter_code      TEXT    NOT NULL,
    scope_type          TEXT    NOT NULL
        CHECK (scope_type IN ('global','store','category','variant','supplier')),
    scope_id            TEXT    NOT NULL DEFAULT '',   -- '' for global
    version             INTEGER NOT NULL,
    value_number        INTEGER,
    value_text          TEXT,
    unit_code           TEXT    REFERENCES units_of_measure(unit_code),
    interval_low        INTEGER,
    interval_high       INTEGER,
    method              TEXT    NOT NULL,      -- appears in the Because block
    source              TEXT    NOT NULL
        CHECK (source IN ('engine','cold_start_default','manual_override')),
    observation_count   INTEGER NOT NULL DEFAULT 0,    -- drives honest degradation
    computed_at         TEXT    NOT NULL,      -- the age Almanac admits to
    is_current          INTEGER NOT NULL DEFAULT 1 CHECK (is_current IN (0,1)),
    PRIMARY KEY (parameter_code, scope_type, scope_id, version),
    CHECK (value_number IS NOT NULL OR value_text IS NOT NULL),
    CHECK (interval_low IS NULL OR interval_high IS NULL OR interval_high >= interval_low)
) STRICT;

CREATE UNIQUE INDEX ux_parameter_current
    ON parameter_registry(parameter_code, scope_type, scope_id) WHERE is_current = 1;

CREATE TABLE roles (
    role_code TEXT PRIMARY KEY,
    rank INTEGER NOT NULL UNIQUE CHECK (rank > 0),
    label_ar TEXT NOT NULL,
    label_fr TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1 CHECK(is_active IN (0,1)),
    created_at TEXT NOT NULL
) STRICT;

INSERT INTO roles (role_code, rank, label_ar, label_fr, is_active, created_at) VALUES
    ('cashier',    10, 'أمين صندوق', 'Caissier',    1, datetime('now')),
    ('supervisor', 20, 'مشرف',       'Superviseur', 1, datetime('now')),
    ('manager',    30, 'مدير',       'Gérant',      1, datetime('now')),
    ('owner',      40, 'مالك',       'Propriétaire',1, datetime('now')),
    ('admin',      50, 'مسؤول',      'Administrateur',1, datetime('now'));

CREATE TABLE recommendations (
    recommendation_id   TEXT    PRIMARY KEY,   -- idempotency key across both Admin surfaces
    store_id            TEXT    NOT NULL REFERENCES stores(store_id),
    department          TEXT    NOT NULL
        CHECK (department IN ('inventory','sales_demand','supply','planning','customer')),
    urgency             TEXT    NOT NULL
        CHECK (urgency IN ('quiet','standard','warning','critical')),
    action_type         TEXT    NOT NULL CHECK (action_type IN ('binary','menu')),
    subject_type        TEXT    NOT NULL
        CHECK (subject_type IN ('variant','batch','product','supplier','customer','store')),
    subject_id          TEXT    NOT NULL,
    headline            TEXT    NOT NULL,
    because_json        TEXT    NOT NULL,      -- at most three reasons, each with a figure
    interval_low        INTEGER,
    interval_high       INTEGER,
    computed_at         TEXT    NOT NULL,
    parameter_version   INTEGER,
    source              TEXT    NOT NULL,
    minimum_required_role       TEXT    NOT NULL REFERENCES roles(role_code),
    status              TEXT    NOT NULL DEFAULT 'pending'
        CHECK (status IN ('pending','delivered','decided','expired','superseded')),
    issued_at           TEXT    NOT NULL,
    delivered_at        TEXT,
    expires_at          TEXT,
    CHECK (interval_low IS NULL OR interval_high IS NULL OR interval_high >= interval_low)
) STRICT;

CREATE TABLE recommendation_options (
    option_id           TEXT    PRIMARY KEY,
    recommendation_id   TEXT    NOT NULL REFERENCES recommendations(recommendation_id) ON DELETE CASCADE,
    label               TEXT    NOT NULL,
    display_order       INTEGER NOT NULL DEFAULT 0,
    payload_json        TEXT    NOT NULL,
    projected_value     INTEGER,        -- e.g. expected recovery at this markdown stage
    UNIQUE (recommendation_id, display_order)
) STRICT;

-- Append-only. Feeds revealed-preference tuning: the decision is itself a signal.
CREATE TABLE recommendation_decisions (
    decision_id             TEXT    PRIMARY KEY,
    recommendation_id       TEXT    NOT NULL REFERENCES recommendations(recommendation_id),
    decision                TEXT    NOT NULL
        CHECK (decision IN ('accept','adjust','dismiss','snooze')),
    chosen_option_id        TEXT    REFERENCES recommendation_options(option_id),
    adjusted_payload_json   TEXT,
    snooze_until            TEXT,
    origin                  TEXT    NOT NULL CHECK (origin IN ('store','cloud')),
    decided_by              TEXT    REFERENCES staff(staff_id),
    decided_at              TEXT    NOT NULL,
    terminal_id             TEXT    REFERENCES terminals(terminal_id),
    applied_at              TEXT,
    resulting_entity_type   TEXT,
    resulting_entity_id     TEXT,
    CHECK (decision <> 'snooze' OR snooze_until IS NOT NULL),
    CHECK (decision <> 'adjust' OR adjusted_payload_json IS NOT NULL)
) STRICT;

-- =============================================================================
-- SECTION 11 — SYNC
-- The outbox lives in THIS file specifically because it commits in the same
-- transaction as the domain row. That is the whole point of the pattern.
-- =============================================================================

CREATE TABLE outbox (
    outbox_id           TEXT    PRIMARY KEY,
    sequence_number     INTEGER NOT NULL UNIQUE,    -- gapless, monotonic, store-assigned
    channel             TEXT    NOT NULL
        CHECK (channel IN ('A_statistics','B_operational','D_decisions')),
    message_type        TEXT    NOT NULL,
    entity_type         TEXT,
    entity_id           TEXT,
    payload_json        TEXT    NOT NULL,   -- ALREADY PSEUDONYMISED. Never identified.
    is_priority         INTEGER NOT NULL DEFAULT 0 CHECK (is_priority IN (0,1)),
    attempts            INTEGER NOT NULL DEFAULT 0,
    last_attempt_at     TEXT,
    last_error          TEXT,
    created_at          TEXT    NOT NULL
) STRICT;

CREATE TABLE inbox (
    inbox_id            TEXT    PRIMARY KEY,
    message_id          TEXT    NOT NULL UNIQUE,    -- cloud-assigned; the dedupe key
    cloud_sequence      INTEGER NOT NULL,
    channel             TEXT    NOT NULL
        CHECK (channel IN ('C_recommendations','D_intents','E_control','F_parameters')),
    message_type        TEXT    NOT NULL,
    payload_json        TEXT    NOT NULL,
    received_at         TEXT    NOT NULL,
    applied_at          TEXT,
    status              TEXT    NOT NULL DEFAULT 'pending'
        CHECK (status IN ('pending','applied','duplicate','rejected')),
    rejection_reason    TEXT
) STRICT;

CREATE TABLE sync_state (
    state_key           TEXT    PRIMARY KEY,
    state_value         TEXT    NOT NULL,
    updated_at          TEXT    NOT NULL
) STRICT;

-- The only channel with real difficulty. A rejection is NEVER silent:
-- fresh_request_id points at the pending-queue entry it generated.
CREATE TABLE intents (
    intent_id               TEXT    PRIMARY KEY,   -- assigned in the cloud
    intent_type             TEXT    NOT NULL
        CHECK (intent_type IN ('accept_reorder','markdown_stage','adjust_quantity','dismiss',
                                'price_change','promotion','catalogue_edit','supplier_edit',
                                'rights_action')), -- we may add other options
    store_id                TEXT    NOT NULL REFERENCES stores(store_id),
    payload_json            TEXT    NOT NULL,
    preconditions_json      TEXT    NOT NULL,
    created_at_cloud        TEXT    NOT NULL,
    expires_at              TEXT,               -- NULL = never expires
    received_at             TEXT    NOT NULL,
    evaluated_at            TEXT,
    status                  TEXT    NOT NULL DEFAULT 'pending'
        CHECK (status IN ('pending','applied','rejected_stale','rejected_invalid')),
    rejection_reason        TEXT,
    fresh_request_id        TEXT    REFERENCES recommendations(recommendation_id),
    decided_by_cloud_user   TEXT,
    CHECK (status NOT IN ('rejected_stale','rejected_invalid') OR rejection_reason IS NOT NULL)
) STRICT;

-- =============================================================================
-- SECTION 12 — INDEXES
-- SQLite does NOT auto-index foreign keys. Every FK used in a join or a
-- cascade needs one, and every hot query path needs its own.
-- =============================================================================

-- Catalogue: the barcode lookup is the single hottest query in the system
CREATE INDEX ix_variants_product         ON variants(product_id);
CREATE INDEX ix_variants_status          ON variants(status);
CREATE INDEX ix_categories_parent        ON categories(parent_id);
CREATE INDEX ix_product_category_cat     ON product_category(category_id);
CREATE INDEX ix_pav_attribute            ON product_attribute_values(attribute_code);
CREATE INDEX ix_vav_attribute            ON variant_attribute_values(attribute_code);
CREATE INDEX ix_category_attr_attribute  ON category_attributes(attribute_code);

-- Pricing: the current price for a variant at a store
CREATE INDEX ix_prices_lookup            ON prices(variant_id, store_id, valid_from DESC);
CREATE INDEX ix_promo_variant_variant    ON promotion_variant(variant_id);
CREATE INDEX ix_promo_product_product    ON promotion_product(product_id);
CREATE INDEX ix_promotions_status        ON promotions(status, store_id);
CREATE INDEX ix_bundle_items_variant     ON product_bundle_items(variant_id);

-- Supply
CREATE INDEX ix_supplier_variant_variant ON supplier_variant(variant_id);
CREATE INDEX ix_po_store_status          ON purchase_orders(store_id, status);
CREATE INDEX ix_po_supplier              ON purchase_orders(supplier_id);
CREATE INDEX ix_po_items_variant         ON purchase_order_items(variant_id);

-- Inventory: expiry detection scans this, and it must stay fast with no history
CREATE INDEX ix_batches_expiry           ON batches(store_id, expiration_date)
    WHERE expiration_date IS NOT NULL AND status = 'active';
CREATE INDEX ix_batches_product          ON batches(product_id);
CREATE INDEX ix_batches_order            ON batches(order_id);
CREATE INDEX ix_batch_items_variant      ON batch_items(variant_id);
CREATE INDEX ix_inventories_variant      ON inventories(variant_id, store_id);
CREATE INDEX ix_inventories_batch        ON inventories(batch_id);
CREATE INDEX ix_movements_variant_date   ON stock_movements(variant_id, movement_date);
CREATE INDEX ix_movements_batch          ON stock_movements(batch_id);
CREATE INDEX ix_movements_reference      ON stock_movements(reference_type, reference_id);
CREATE INDEX ix_movements_store_date     ON stock_movements(store_id, movement_date);
CREATE INDEX ix_count_items_count        ON stock_count_items(count_id);
CREATE INDEX ix_stock_counts_store       ON stock_counts(store_id, status);

-- Customers and compliance
CREATE INDEX ix_customers_phone          ON customers(contact_phone);
CREATE INDEX ix_consent_customer         ON consent_events(customer_id, occurred_at);
CREATE INDEX ix_dsr_customer             ON data_subject_requests(customer_id);
CREATE INDEX ix_dsr_open                 ON data_subject_requests(due_at) WHERE status IN ('open','in_progress');
CREATE INDEX ix_proclog_subject          ON processing_log(subject_type, subject_id, occurred_at);
CREATE INDEX ix_proclog_occurred         ON processing_log(occurred_at);
CREATE INDEX ix_erasure_subject          ON erasure_ledger(subject_type, subject_id);

-- Sales: date-ranged reporting is the dominant read pattern
CREATE INDEX ix_transactions_store_date  ON transactions(store_id, occurred_at);
CREATE INDEX ix_transactions_customer    ON transactions(customer_id) WHERE customer_id IS NOT NULL;
CREATE INDEX ix_transactions_session     ON transactions(cash_session_id);
CREATE INDEX ix_transactions_staff       ON transactions(staff_id, occurred_at);
CREATE INDEX ix_transactions_original    ON transactions(original_transaction_id);
CREATE INDEX ix_txn_items_transaction    ON transaction_items(transaction_id);
CREATE INDEX ix_txn_items_variant        ON transaction_items(variant_id, created_at);
CREATE INDEX ix_txn_items_batch          ON transaction_items(batch_id);
CREATE INDEX ix_payments_transaction     ON transaction_payments(transaction_id);
CREATE INDEX ix_payments_method          ON transaction_payments(payment_method, created_at);
CREATE INDEX ix_returns_item             ON returns(transaction_item_id);
CREATE INDEX ix_returns_store_date       ON returns(store_id, created_at);

-- Ledgers
CREATE INDEX ix_loyalty_customer         ON loyalty_movements(customer_id, occurred_at);
CREATE INDEX ix_credit_customer          ON credit_movements(customer_id, occurred_at);

-- Organisation
CREATE INDEX ix_staff_store              ON staff(store_id, status);
CREATE INDEX ix_terminals_store          ON terminals(store_id);
CREATE INDEX ix_shifts_staff             ON shifts(staff_id, start_time);
CREATE INDEX ix_shifts_store_open        ON shifts(store_id) WHERE status = 'open';
CREATE INDEX ix_cash_sessions_terminal   ON cash_sessions(terminal_id, opened_at);
CREATE INDEX ix_cash_sessions_open       ON cash_sessions(store_id) WHERE status = 'open';
CREATE INDEX ix_cash_movements_session   ON cash_movements(session_id);

-- Engine
CREATE INDEX ix_recs_pending             ON recommendations(store_id, status, urgency)
    WHERE status IN ('pending','delivered');
CREATE INDEX ix_recs_subject             ON recommendations(subject_type, subject_id);
CREATE INDEX ix_rec_options_rec          ON recommendation_options(recommendation_id);
CREATE INDEX ix_rec_decisions_rec        ON recommendation_decisions(recommendation_id);
CREATE INDEX ix_rec_decisions_date       ON recommendation_decisions(decided_at);

-- Sync: the drain reads the outbox in sequence order and must stay cheap
CREATE INDEX ix_outbox_drain             ON outbox(is_priority DESC, sequence_number);
CREATE INDEX ix_inbox_pending            ON inbox(status, cloud_sequence) WHERE status = 'pending';
CREATE INDEX ix_intents_pending          ON intents(store_id, status) WHERE status = 'pending';
CREATE INDEX ix_intents_expiry           ON intents(expires_at) WHERE status = 'pending';

-- =============================================================================
-- SECTION 13 — APPEND-ONLY ENFORCEMENT
-- These tables are audit trails or financial ledgers. A correction is a new
-- row, never an edit. The database enforces it so no call site has to remember.
--
-- processing_log is deliberately NOT protected against UPDATE: erasure must
-- purge its identity columns.
-- outbox is deliberately NOT protected: rows are deleted after acknowledgement.
-- =============================================================================

CREATE TRIGGER trg_consent_events_no_update
BEFORE UPDATE ON consent_events
BEGIN
    SELECT RAISE(ABORT, 'consent_events is append-only: withdrawal is a new event');
END;

CREATE TRIGGER trg_consent_events_no_delete
BEFORE DELETE ON consent_events
BEGIN
    SELECT RAISE(ABORT, 'consent_events is append-only');
END;

CREATE TRIGGER trg_stock_movements_no_update
BEFORE UPDATE ON stock_movements
BEGIN
    SELECT RAISE(ABORT, 'stock_movements is append-only: post a correcting movement');
END;

CREATE TRIGGER trg_stock_movements_no_delete
BEFORE DELETE ON stock_movements
BEGIN
    SELECT RAISE(ABORT, 'stock_movements is append-only');
END;

CREATE TRIGGER trg_loyalty_movements_no_update
BEFORE UPDATE ON loyalty_movements
BEGIN
    SELECT RAISE(ABORT, 'loyalty_movements is append-only: post a reversing movement');
END;

CREATE TRIGGER trg_loyalty_movements_no_delete
BEFORE DELETE ON loyalty_movements
BEGIN
    SELECT RAISE(ABORT, 'loyalty_movements is append-only');
END;

CREATE TRIGGER trg_credit_movements_no_update
BEFORE UPDATE ON credit_movements
BEGIN
    SELECT RAISE(ABORT, 'credit_movements is append-only: post a reversing movement');
END;

CREATE TRIGGER trg_credit_movements_no_delete
BEFORE DELETE ON credit_movements
BEGIN
    SELECT RAISE(ABORT, 'credit_movements is append-only');
END;

CREATE TRIGGER trg_rec_decisions_no_update
BEFORE UPDATE ON recommendation_decisions
    WHEN OLD.applied_at IS NOT NULL
BEGIN
    SELECT RAISE(ABORT, 'a decision cannot be changed once applied');
END;

CREATE TRIGGER trg_rec_decisions_no_delete
BEFORE DELETE ON recommendation_decisions
BEGIN
    SELECT RAISE(ABORT, 'recommendation_decisions is append-only');
END;

CREATE TRIGGER trg_erasure_ledger_no_delete
BEFORE DELETE ON erasure_ledger
BEGIN
    SELECT RAISE(ABORT, 'erasure_ledger is the evidence of compliance and cannot be deleted');
END;

-- =============================================================================
-- SECTION 14 — SCHEMA VERSION
-- =============================================================================

CREATE TABLE schema_migrations (
    version             TEXT    PRIMARY KEY,
    description         TEXT    NOT NULL,
    applied_at          TEXT    NOT NULL
) STRICT;

INSERT INTO schema_migrations (version, description, applied_at)
VALUES ('v7.0.0', 'Initial Waymark operational schema', datetime('now'));

INSERT INTO schema_migrations (version, description, applied_at)
VALUES ('v7.1.0', 'Waymark operational schema v7.1: roles lookup, barcode types, lot tracking', datetime('now'));

COMMIT;

-- =============================================================================
-- POST-CREATION CHECKS
-- Run these once after creating the file. All three should return nothing.
-- =============================================================================
--
--   PRAGMA integrity_check;
--   PRAGMA foreign_key_check;
--   SELECT name FROM sqlite_master WHERE type='table' AND sql NOT LIKE '%STRICT%';
--
-- =============================================================================
-- SEED DATA
-- Not included here. Seeded per store type at onboarding:
--   units_of_measure    - unit, kg, g, L, mL, case, box
--   reason_codes        - per store type
--   notice_versions     - the Article 32 notice, ar and fr
--   retention_policies  - per jurisdiction
--   system_config       - tax_rate_default, credit_ceiling_offline, backup_schedule
--   attribute_definitions and category_attributes - per store type
-- =============================================================================
