using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Waymark.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    category_id = table.Column<string>(type: "TEXT", nullable: false),
                    category_name = table.Column<string>(type: "TEXT", nullable: false),
                    parent_id = table.Column<string>(type: "TEXT", nullable: true),
                    slug = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    image = table.Column<string>(type: "TEXT", nullable: true),
                    tax_rate = table.Column<long>(type: "INTEGER", nullable: true),
                    sensitive_flag = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    sensitive_reason = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "active"),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.category_id);
                    table.CheckConstraint("ck_categories_parent_id", "parent_id IS NULL OR parent_id <> category_id");
                    table.CheckConstraint("ck_categories_sensitive_flag", "sensitive_flag IN (0,1)");
                    table.CheckConstraint("ck_categories_sensitive_flag_2", "sensitive_flag = 0 OR sensitive_reason IS NOT NULL");
                    table.CheckConstraint("ck_categories_status", "status IN ('active','archived')");
                    table.CheckConstraint("ck_categories_tax_rate", "tax_rate IS NULL OR tax_rate BETWEEN 0 AND 10000");
                    table.ForeignKey(
                        name: "FK_categories_categories_parent_id",
                        column: x => x.parent_id,
                        principalTable: "categories",
                        principalColumn: "category_id");
                });

            migrationBuilder.CreateTable(
                name: "inbox",
                columns: table => new
                {
                    inbox_id = table.Column<string>(type: "TEXT", nullable: false),
                    message_id = table.Column<string>(type: "TEXT", nullable: false),
                    cloud_sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    channel = table.Column<string>(type: "TEXT", nullable: false),
                    message_type = table.Column<string>(type: "TEXT", nullable: false),
                    payload_json = table.Column<string>(type: "TEXT", nullable: false),
                    received_at = table.Column<string>(type: "TEXT", nullable: false),
                    applied_at = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "pending"),
                    rejection_reason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox", x => x.inbox_id);
                    table.CheckConstraint("ck_inbox_channel", "channel IN ('C_recommendations','D_intents','E_control','F_parameters')");
                    table.CheckConstraint("ck_inbox_status", "status IN ('pending','applied','duplicate','rejected')");
                });

            migrationBuilder.CreateTable(
                name: "notice_versions",
                columns: table => new
                {
                    version_code = table.Column<string>(type: "TEXT", nullable: false),
                    notice_type = table.Column<string>(type: "TEXT", nullable: false),
                    language = table.Column<string>(type: "TEXT", nullable: false),
                    body_text = table.Column<string>(type: "TEXT", nullable: false),
                    effective_from = table.Column<string>(type: "TEXT", nullable: false),
                    effective_to = table.Column<string>(type: "TEXT", nullable: true),
                    published_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notice_versions", x => x.version_code);
                    table.CheckConstraint("ck_notice_versions_effective_to", "effective_to IS NULL OR effective_to > effective_from");
                    table.CheckConstraint("ck_notice_versions_language", "language IN ('ar','fr','en')");
                    table.CheckConstraint("ck_notice_versions_notice_type", "notice_type IN ('processing','marketing','staff')");
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                columns: table => new
                {
                    outbox_id = table.Column<string>(type: "TEXT", nullable: false),
                    sequence_number = table.Column<long>(type: "INTEGER", nullable: false),
                    channel = table.Column<string>(type: "TEXT", nullable: false),
                    message_type = table.Column<string>(type: "TEXT", nullable: false),
                    entity_type = table.Column<string>(type: "TEXT", nullable: true),
                    entity_id = table.Column<string>(type: "TEXT", nullable: true),
                    payload_json = table.Column<string>(type: "TEXT", nullable: false),
                    is_priority = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    attempts = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    last_attempt_at = table.Column<string>(type: "TEXT", nullable: true),
                    last_error = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox", x => x.outbox_id);
                    table.CheckConstraint("ck_outbox_channel", "channel IN ('A_statistics','B_operational','D_decisions')");
                    table.CheckConstraint("ck_outbox_is_priority", "is_priority IN (0,1)");
                });

            migrationBuilder.CreateTable(
                name: "product_bundles",
                columns: table => new
                {
                    bundle_id = table.Column<string>(type: "TEXT", nullable: false),
                    barcode = table.Column<string>(type: "TEXT", nullable: true),
                    bundle_name = table.Column<string>(type: "TEXT", nullable: false),
                    bundle_type = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "active"),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_bundles", x => x.bundle_id);
                    table.CheckConstraint("ck_product_bundles_bundle_type", "bundle_type IN ('fixed','mix_and_match')");
                    table.CheckConstraint("ck_product_bundles_status", "status IN ('active','inactive','archived')");
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    product_id = table.Column<string>(type: "TEXT", nullable: false),
                    product_name = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "active"),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.product_id);
                    table.CheckConstraint("ck_products_status", "status IN ('active','discontinued','archived')");
                });

            migrationBuilder.CreateTable(
                name: "reason_codes",
                columns: table => new
                {
                    reason_code = table.Column<string>(type: "TEXT", nullable: false),
                    applies_to = table.Column<string>(type: "TEXT", nullable: false),
                    label_ar = table.Column<string>(type: "TEXT", nullable: false),
                    label_fr = table.Column<string>(type: "TEXT", nullable: false),
                    requires_note = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    requires_manager = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    display_order = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reason_codes", x => x.reason_code);
                    table.CheckConstraint("ck_reason_codes_applies_to", "applies_to IN ('discount','price_override','adjustment','void', 'return','no_sale','cash_movement','write_off')");
                    table.CheckConstraint("ck_reason_codes_is_active", "is_active IN (0,1)");
                    table.CheckConstraint("ck_reason_codes_requires_manager", "requires_manager IN (0,1)");
                    table.CheckConstraint("ck_reason_codes_requires_note", "requires_note IN (0,1)");
                });

            migrationBuilder.CreateTable(
                name: "retention_policies",
                columns: table => new
                {
                    policy_code = table.Column<string>(type: "TEXT", nullable: false),
                    entity_type = table.Column<string>(type: "TEXT", nullable: false),
                    retention_days = table.Column<long>(type: "INTEGER", nullable: false),
                    legal_basis_reference = table.Column<string>(type: "TEXT", nullable: false),
                    action_on_expiry = table.Column<string>(type: "TEXT", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_retention_policies", x => x.policy_code);
                    table.CheckConstraint("ck_retention_policies_action_on_expiry", "action_on_expiry IN ('delete','unlink','archive')");
                    table.CheckConstraint("ck_retention_policies_entity_type", "entity_type IN ('transaction','customer','staff','processing_log', 'consent_event','recommendation','stock_movement')");
                    table.CheckConstraint("ck_retention_policies_is_active", "is_active IN (0,1)");
                    table.CheckConstraint("ck_retention_policies_retention_days", "retention_days > 0");
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    role_code = table.Column<string>(type: "TEXT", nullable: false),
                    rank = table.Column<long>(type: "INTEGER", nullable: false),
                    label_ar = table.Column<string>(type: "TEXT", nullable: false),
                    label_fr = table.Column<string>(type: "TEXT", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.role_code);
                    table.CheckConstraint("ck_roles_is_active", "is_active IN (0,1)");
                    table.CheckConstraint("ck_roles_rank", "rank > 0");
                });

            migrationBuilder.CreateTable(
                name: "schema_migrations",
                columns: table => new
                {
                    version = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    applied_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schema_migrations", x => x.version);
                });

            migrationBuilder.CreateTable(
                name: "store_entitlements",
                columns: table => new
                {
                    entitlement_code = table.Column<string>(type: "TEXT", nullable: false),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    tier = table.Column<string>(type: "TEXT", nullable: true),
                    valid_from = table.Column<string>(type: "TEXT", nullable: true),
                    valid_to = table.Column<string>(type: "TEXT", nullable: true),
                    synced_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_entitlements", x => x.entitlement_code);
                    table.CheckConstraint("ck_store_entitlements_is_enabled", "is_enabled IN (0,1)");
                    table.CheckConstraint("ck_store_entitlements_tier", "tier IN ('basic','pro','enterprise')");
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    supplier_id = table.Column<string>(type: "TEXT", nullable: false),
                    supplier_code = table.Column<string>(type: "TEXT", nullable: false),
                    company_name = table.Column<string>(type: "TEXT", nullable: false),
                    responsible_name = table.Column<string>(type: "TEXT", nullable: true),
                    contact_phone = table.Column<string>(type: "TEXT", nullable: true),
                    email = table.Column<string>(type: "TEXT", nullable: true),
                    website = table.Column<string>(type: "TEXT", nullable: true),
                    shipping_address = table.Column<string>(type: "TEXT", nullable: true),
                    net_days = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    credit_limit = table.Column<long>(type: "INTEGER", nullable: true),
                    currency = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "DZD"),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "active"),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppliers", x => x.supplier_id);
                    table.CheckConstraint("ck_suppliers_net_days", "net_days >= 0");
                    table.CheckConstraint("ck_suppliers_status", "status IN ('active','inactive','blacklisted')");
                });

            migrationBuilder.CreateTable(
                name: "sync_state",
                columns: table => new
                {
                    state_key = table.Column<string>(type: "TEXT", nullable: false),
                    state_value = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_state", x => x.state_key);
                });

            migrationBuilder.CreateTable(
                name: "system_config",
                columns: table => new
                {
                    config_key = table.Column<string>(type: "TEXT", nullable: false),
                    config_value = table.Column<string>(type: "TEXT", nullable: false),
                    data_type = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    updated_by = table.Column<string>(type: "TEXT", nullable: true),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_config", x => x.config_key);
                    table.CheckConstraint("ck_system_config_data_type", "data_type IN ('text','integer','money','percent','bool','date')");
                });

            migrationBuilder.CreateTable(
                name: "units_of_measure",
                columns: table => new
                {
                    unit_code = table.Column<string>(type: "TEXT", nullable: false),
                    name_ar = table.Column<string>(type: "TEXT", nullable: false),
                    name_fr = table.Column<string>(type: "TEXT", nullable: false),
                    dimension = table.Column<string>(type: "TEXT", nullable: false),
                    base_unit_code = table.Column<string>(type: "TEXT", nullable: true),
                    factor_to_base = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 1000000L),
                    decimal_places = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_units_of_measure", x => x.unit_code);
                    table.CheckConstraint("ck_units_of_measure_decimal_places", "decimal_places BETWEEN 0 AND 3");
                    table.CheckConstraint("ck_units_of_measure_dimension", "dimension IN ('count','weight','volume','length')");
                    table.CheckConstraint("ck_units_of_measure_factor_to_base", "factor_to_base > 0");
                    table.CheckConstraint("ck_units_of_measure_is_active", "is_active IN (0,1)");
                    table.ForeignKey(
                        name: "FK_units_of_measure_units_of_measure_base_unit_code",
                        column: x => x.base_unit_code,
                        principalTable: "units_of_measure",
                        principalColumn: "unit_code");
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    customer_id = table.Column<string>(type: "TEXT", nullable: false),
                    customer_name = table.Column<string>(type: "TEXT", nullable: false),
                    contact_phone = table.Column<string>(type: "TEXT", nullable: true),
                    email = table.Column<string>(type: "TEXT", nullable: true),
                    preferred_language = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "ar"),
                    join_date = table.Column<string>(type: "TEXT", nullable: false),
                    last_order_date = table.Column<string>(type: "TEXT", nullable: true),
                    points = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    credit = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    discount = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    tier_ranking = table.Column<string>(type: "TEXT", nullable: true),
                    ecommerce_flag = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    legal_basis = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "contract"),
                    consent_profiling = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    consent_profiling_at = table.Column<string>(type: "TEXT", nullable: true),
                    consent_profiling_notice_version = table.Column<string>(type: "TEXT", nullable: true),
                    consent_marketing = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    consent_marketing_at = table.Column<string>(type: "TEXT", nullable: true),
                    consent_marketing_notice_version = table.Column<string>(type: "TEXT", nullable: true),
                    objection_flag = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    deletion_requested_at = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "active"),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.customer_id);
                    table.CheckConstraint("ck_customers_consent_marketing", "consent_marketing IN (0,1)");
                    table.CheckConstraint("ck_customers_consent_marketing_2", "consent_marketing = 0 OR consent_marketing_at IS NOT NULL");
                    table.CheckConstraint("ck_customers_consent_profiling", "consent_profiling IN (0,1)");
                    table.CheckConstraint("ck_customers_consent_profiling_2", "consent_profiling = 0 OR consent_profiling_at IS NOT NULL");
                    table.CheckConstraint("ck_customers_discount", "discount BETWEEN 0 AND 10000");
                    table.CheckConstraint("ck_customers_ecommerce_flag", "ecommerce_flag IN (0,1)");
                    table.CheckConstraint("ck_customers_legal_basis", "legal_basis IN ('consent','contract','legal_obligation','legitimate_interest')");
                    table.CheckConstraint("ck_customers_objection_flag", "objection_flag IN (0,1)");
                    table.CheckConstraint("ck_customers_preferred_language", "preferred_language IN ('ar','fr','en')");
                    table.CheckConstraint("ck_customers_status", "status IN ('active','inactive','erased')");
                    table.ForeignKey(
                        name: "FK_customers_notice_versions_consent_marketing_notice_version",
                        column: x => x.consent_marketing_notice_version,
                        principalTable: "notice_versions",
                        principalColumn: "version_code");
                    table.ForeignKey(
                        name: "FK_customers_notice_versions_consent_profiling_notice_version",
                        column: x => x.consent_profiling_notice_version,
                        principalTable: "notice_versions",
                        principalColumn: "version_code");
                });

            migrationBuilder.CreateTable(
                name: "product_category",
                columns: table => new
                {
                    product_id = table.Column<string>(type: "TEXT", nullable: false),
                    category_id = table.Column<string>(type: "TEXT", nullable: false),
                    is_primary = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    added_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_category", x => new { x.product_id, x.category_id });
                    table.CheckConstraint("ck_product_category_is_primary", "is_primary IN (0,1)");
                    table.ForeignKey(
                        name: "FK_product_category_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "category_id");
                    table.ForeignKey(
                        name: "FK_product_category_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "product_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attribute_definitions",
                columns: table => new
                {
                    attribute_code = table.Column<string>(type: "TEXT", nullable: false),
                    label_ar = table.Column<string>(type: "TEXT", nullable: false),
                    label_fr = table.Column<string>(type: "TEXT", nullable: false),
                    label_en = table.Column<string>(type: "TEXT", nullable: true),
                    data_type = table.Column<string>(type: "TEXT", nullable: false),
                    unit_code = table.Column<string>(type: "TEXT", nullable: true),
                    applies_to = table.Column<string>(type: "TEXT", nullable: false),
                    is_groupable = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    is_filterable = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    help_text = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "active"),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attribute_definitions", x => x.attribute_code);
                    table.CheckConstraint("ck_attribute_definitions_applies_to", "applies_to IN ('product','variant')");
                    table.CheckConstraint("ck_attribute_definitions_data_type", "data_type IN ('text','number','bool','date','enum')");
                    table.CheckConstraint("ck_attribute_definitions_is_filterable", "is_filterable IN (0,1)");
                    table.CheckConstraint("ck_attribute_definitions_is_groupable", "is_groupable IN (0,1)");
                    table.CheckConstraint("ck_attribute_definitions_status", "status IN ('active','archived')");
                    table.CheckConstraint("ck_attribute_definitions_unit_code", "unit_code IS NULL OR data_type = 'number'");
                    table.ForeignKey(
                        name: "FK_attribute_definitions_units_of_measure_unit_code",
                        column: x => x.unit_code,
                        principalTable: "units_of_measure",
                        principalColumn: "unit_code");
                });

            migrationBuilder.CreateTable(
                name: "parameter_registry",
                columns: table => new
                {
                    parameter_code = table.Column<string>(type: "TEXT", nullable: false),
                    scope_type = table.Column<string>(type: "TEXT", nullable: false),
                    scope_id = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    version = table.Column<long>(type: "INTEGER", nullable: false),
                    value_number = table.Column<long>(type: "INTEGER", nullable: true),
                    value_text = table.Column<string>(type: "TEXT", nullable: true),
                    unit_code = table.Column<string>(type: "TEXT", nullable: true),
                    interval_low = table.Column<long>(type: "INTEGER", nullable: true),
                    interval_high = table.Column<long>(type: "INTEGER", nullable: true),
                    method = table.Column<string>(type: "TEXT", nullable: false),
                    source = table.Column<string>(type: "TEXT", nullable: false),
                    observation_count = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    computed_at = table.Column<string>(type: "TEXT", nullable: false),
                    is_current = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parameter_registry", x => new { x.parameter_code, x.scope_type, x.scope_id, x.version });
                    table.CheckConstraint("ck_parameter_registry_interval_low", "interval_low IS NULL OR interval_high IS NULL OR interval_high >= interval_low");
                    table.CheckConstraint("ck_parameter_registry_is_current", "is_current IN (0,1)");
                    table.CheckConstraint("ck_parameter_registry_scope_type", "scope_type IN ('global','store','category','variant','supplier')");
                    table.CheckConstraint("ck_parameter_registry_source", "source IN ('engine','cold_start_default','manual_override')");
                    table.CheckConstraint("ck_parameter_registry_value_number", "value_number IS NOT NULL OR value_text IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_parameter_registry_units_of_measure_unit_code",
                        column: x => x.unit_code,
                        principalTable: "units_of_measure",
                        principalColumn: "unit_code");
                });

            migrationBuilder.CreateTable(
                name: "variants",
                columns: table => new
                {
                    variant_id = table.Column<string>(type: "TEXT", nullable: false),
                    product_id = table.Column<string>(type: "TEXT", nullable: false),
                    variant_name = table.Column<string>(type: "TEXT", nullable: false),
                    barcode = table.Column<string>(type: "TEXT", nullable: true),
                    plu = table.Column<string>(type: "TEXT", nullable: true),
                    sku = table.Column<string>(type: "TEXT", nullable: true),
                    barcode_type = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "standard"),
                    selling_unit_code = table.Column<string>(type: "TEXT", nullable: false),
                    is_weighted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    tare_weight = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    image = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "active"),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_variants", x => x.variant_id);
                    table.CheckConstraint("ck_variants_barcode_type", "barcode_type IN ('standard', 'weight_embedded', 'price_embedded')");
                    table.CheckConstraint("ck_variants_is_weighted", "is_weighted IN (0,1)");
                    table.CheckConstraint("ck_variants_is_weighted_2", "is_weighted = 0 OR plu IS NOT NULL OR barcode IS NOT NULL");
                    table.CheckConstraint("ck_variants_status", "status IN ('active','discontinued','archived')");
                    table.CheckConstraint("ck_variants_tare_weight", "tare_weight >= 0");
                    table.ForeignKey(
                        name: "FK_variants_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "product_id");
                    table.ForeignKey(
                        name: "FK_variants_units_of_measure_selling_unit_code",
                        column: x => x.selling_unit_code,
                        principalTable: "units_of_measure",
                        principalColumn: "unit_code");
                });

            migrationBuilder.CreateTable(
                name: "attribute_options",
                columns: table => new
                {
                    attribute_code = table.Column<string>(type: "TEXT", nullable: false),
                    option_code = table.Column<string>(type: "TEXT", nullable: false),
                    label_ar = table.Column<string>(type: "TEXT", nullable: false),
                    label_fr = table.Column<string>(type: "TEXT", nullable: false),
                    display_order = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attribute_options", x => new { x.attribute_code, x.option_code });
                    table.CheckConstraint("ck_attribute_options_is_active", "is_active IN (0,1)");
                    table.ForeignKey(
                        name: "FK_attribute_options_attribute_definitions_attribute_code",
                        column: x => x.attribute_code,
                        principalTable: "attribute_definitions",
                        principalColumn: "attribute_code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "category_attributes",
                columns: table => new
                {
                    category_id = table.Column<string>(type: "TEXT", nullable: false),
                    attribute_code = table.Column<string>(type: "TEXT", nullable: false),
                    is_required = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    display_order = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    inherits_to_children = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_category_attributes", x => new { x.category_id, x.attribute_code });
                    table.CheckConstraint("ck_category_attributes_inherits_to_children", "inherits_to_children IN (0,1)");
                    table.CheckConstraint("ck_category_attributes_is_required", "is_required IN (0,1)");
                    table.ForeignKey(
                        name: "FK_category_attributes_attribute_definitions_attribute_code",
                        column: x => x.attribute_code,
                        principalTable: "attribute_definitions",
                        principalColumn: "attribute_code",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_category_attributes_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "category_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_bundle_items",
                columns: table => new
                {
                    bundle_item_id = table.Column<string>(type: "TEXT", nullable: false),
                    bundle_id = table.Column<string>(type: "TEXT", nullable: false),
                    variant_id = table.Column<string>(type: "TEXT", nullable: false),
                    quantity = table.Column<long>(type: "INTEGER", nullable: false),
                    valid_from = table.Column<string>(type: "TEXT", nullable: false),
                    valid_to = table.Column<string>(type: "TEXT", nullable: true),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "active")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_bundle_items", x => x.bundle_item_id);
                    table.CheckConstraint("ck_product_bundle_items_quantity", "quantity > 0");
                    table.CheckConstraint("ck_product_bundle_items_status", "status IN ('active','inactive')");
                    table.CheckConstraint("ck_product_bundle_items_valid_to", "valid_to IS NULL OR valid_to > valid_from");
                    table.ForeignKey(
                        name: "FK_product_bundle_items_product_bundles_bundle_id",
                        column: x => x.bundle_id,
                        principalTable: "product_bundles",
                        principalColumn: "bundle_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_product_bundle_items_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "variants",
                        principalColumn: "variant_id");
                });

            migrationBuilder.CreateTable(
                name: "supplier_variant",
                columns: table => new
                {
                    supplier_id = table.Column<string>(type: "TEXT", nullable: false),
                    variant_id = table.Column<string>(type: "TEXT", nullable: false),
                    purchase_unit_code = table.Column<string>(type: "TEXT", nullable: false),
                    units_per_purchase_unit = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 1000L),
                    minimum_order_quantity = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    stated_lead_time_days = table.Column<long>(type: "INTEGER", nullable: true),
                    purchase_price = table.Column<long>(type: "INTEGER", nullable: false),
                    currency = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "DZD"),
                    net_days = table.Column<long>(type: "INTEGER", nullable: true),
                    last_price_at = table.Column<string>(type: "TEXT", nullable: true),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_variant", x => new { x.supplier_id, x.variant_id });
                    table.CheckConstraint("ck_supplier_variant_is_active", "is_active IN (0,1)");
                    table.CheckConstraint("ck_supplier_variant_purchase_price", "purchase_price >= 0");
                    table.CheckConstraint("ck_supplier_variant_stated_lead_time_days", "stated_lead_time_days IS NULL OR stated_lead_time_days >= 0");
                    table.CheckConstraint("ck_supplier_variant_units_per_purchase_unit", "units_per_purchase_unit > 0");
                    table.ForeignKey(
                        name: "FK_supplier_variant_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "supplier_id");
                    table.ForeignKey(
                        name: "FK_supplier_variant_units_of_measure_purchase_unit_code",
                        column: x => x.purchase_unit_code,
                        principalTable: "units_of_measure",
                        principalColumn: "unit_code");
                    table.ForeignKey(
                        name: "FK_supplier_variant_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "variants",
                        principalColumn: "variant_id");
                });

            migrationBuilder.CreateTable(
                name: "product_attribute_values",
                columns: table => new
                {
                    product_id = table.Column<string>(type: "TEXT", nullable: false),
                    attribute_code = table.Column<string>(type: "TEXT", nullable: false),
                    value_text = table.Column<string>(type: "TEXT", nullable: true),
                    value_number = table.Column<long>(type: "INTEGER", nullable: true),
                    value_bool = table.Column<long>(type: "INTEGER", nullable: true),
                    value_date = table.Column<string>(type: "TEXT", nullable: true),
                    option_code = table.Column<string>(type: "TEXT", nullable: true),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_attribute_values", x => new { x.product_id, x.attribute_code });
                    table.CheckConstraint("ck_product_attribute_values_CASE", "(CASE WHEN value_text IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN value_number IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN value_bool IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN value_date IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN option_code IS NOT NULL THEN 1 ELSE 0 END) = 1");
                    table.CheckConstraint("ck_product_attribute_values_value_bool", "value_bool IS NULL OR value_bool IN (0,1)");
                    table.ForeignKey(
                        name: "FK_product_attribute_values_attribute_definitions_attribute_code",
                        column: x => x.attribute_code,
                        principalTable: "attribute_definitions",
                        principalColumn: "attribute_code");
                    table.ForeignKey(
                        name: "FK_product_attribute_values_attribute_options_attribute_code_option_code",
                        columns: x => new { x.attribute_code, x.option_code },
                        principalTable: "attribute_options",
                        principalColumns: new[] { "attribute_code", "option_code" });
                    table.ForeignKey(
                        name: "FK_product_attribute_values_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "product_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "variant_attribute_values",
                columns: table => new
                {
                    variant_id = table.Column<string>(type: "TEXT", nullable: false),
                    attribute_code = table.Column<string>(type: "TEXT", nullable: false),
                    value_text = table.Column<string>(type: "TEXT", nullable: true),
                    value_number = table.Column<long>(type: "INTEGER", nullable: true),
                    value_bool = table.Column<long>(type: "INTEGER", nullable: true),
                    value_date = table.Column<string>(type: "TEXT", nullable: true),
                    option_code = table.Column<string>(type: "TEXT", nullable: true),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_variant_attribute_values", x => new { x.variant_id, x.attribute_code });
                    table.CheckConstraint("ck_variant_attribute_values_CASE", "(CASE WHEN value_text IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN value_number IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN value_bool IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN value_date IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN option_code IS NOT NULL THEN 1 ELSE 0 END) = 1");
                    table.CheckConstraint("ck_variant_attribute_values_value_bool", "value_bool IS NULL OR value_bool IN (0,1)");
                    table.ForeignKey(
                        name: "FK_variant_attribute_values_attribute_definitions_attribute_code",
                        column: x => x.attribute_code,
                        principalTable: "attribute_definitions",
                        principalColumn: "attribute_code");
                    table.ForeignKey(
                        name: "FK_variant_attribute_values_attribute_options_attribute_code_option_code",
                        columns: x => new { x.attribute_code, x.option_code },
                        principalTable: "attribute_options",
                        principalColumns: new[] { "attribute_code", "option_code" });
                    table.ForeignKey(
                        name: "FK_variant_attribute_values_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "variants",
                        principalColumn: "variant_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "batch_items",
                columns: table => new
                {
                    batch_id = table.Column<string>(type: "TEXT", nullable: false),
                    variant_id = table.Column<string>(type: "TEXT", nullable: false),
                    quantity_received = table.Column<long>(type: "INTEGER", nullable: false),
                    unit_code = table.Column<string>(type: "TEXT", nullable: false),
                    unit_cost = table.Column<long>(type: "INTEGER", nullable: false),
                    currency = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "DZD"),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_batch_items", x => new { x.batch_id, x.variant_id });
                    table.CheckConstraint("ck_batch_items_quantity_received", "quantity_received > 0");
                    table.CheckConstraint("ck_batch_items_unit_cost", "unit_cost >= 0");
                    table.ForeignKey(
                        name: "FK_batch_items_units_of_measure_unit_code",
                        column: x => x.unit_code,
                        principalTable: "units_of_measure",
                        principalColumn: "unit_code");
                    table.ForeignKey(
                        name: "FK_batch_items_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "variants",
                        principalColumn: "variant_id");
                });

            migrationBuilder.CreateTable(
                name: "batches",
                columns: table => new
                {
                    batch_id = table.Column<string>(type: "TEXT", nullable: false),
                    lot_number = table.Column<string>(type: "TEXT", nullable: true),
                    supplier_document_ref = table.Column<string>(type: "TEXT", nullable: true),
                    product_id = table.Column<string>(type: "TEXT", nullable: false),
                    store_id = table.Column<string>(type: "TEXT", nullable: false),
                    supplier_id = table.Column<string>(type: "TEXT", nullable: true),
                    order_id = table.Column<string>(type: "TEXT", nullable: true),
                    received_date = table.Column<string>(type: "TEXT", nullable: false),
                    expiration_date = table.Column<string>(type: "TEXT", nullable: true),
                    received_by = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "active"),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_batches", x => x.batch_id);
                    table.CheckConstraint("ck_batches_expiration_date", "expiration_date IS NULL OR expiration_date >= received_date");
                    table.CheckConstraint("ck_batches_status", "status IN ('active','depleted','written_off','quarantined')");
                    table.ForeignKey(
                        name: "FK_batches_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "product_id");
                    table.ForeignKey(
                        name: "FK_batches_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "supplier_id");
                });

            migrationBuilder.CreateTable(
                name: "cash_movements",
                columns: table => new
                {
                    movement_id = table.Column<string>(type: "TEXT", nullable: false),
                    session_id = table.Column<string>(type: "TEXT", nullable: false),
                    movement_type = table.Column<string>(type: "TEXT", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    reason_code = table.Column<string>(type: "TEXT", nullable: false),
                    note = table.Column<string>(type: "TEXT", nullable: true),
                    staff_id = table.Column<string>(type: "TEXT", nullable: false),
                    authorised_by = table.Column<string>(type: "TEXT", nullable: true),
                    occurred_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_movements", x => x.movement_id);
                    table.CheckConstraint("ck_cash_movements_amount", "amount > 0");
                    table.CheckConstraint("ck_cash_movements_movement_type", "movement_type IN ('paid_in','paid_out','drop','float_add','float_remove')");
                    table.ForeignKey(
                        name: "FK_cash_movements_reason_codes_reason_code",
                        column: x => x.reason_code,
                        principalTable: "reason_codes",
                        principalColumn: "reason_code");
                });

            migrationBuilder.CreateTable(
                name: "cash_sessions",
                columns: table => new
                {
                    session_id = table.Column<string>(type: "TEXT", nullable: false),
                    store_id = table.Column<string>(type: "TEXT", nullable: false),
                    terminal_id = table.Column<string>(type: "TEXT", nullable: false),
                    opened_by = table.Column<string>(type: "TEXT", nullable: false),
                    opened_at = table.Column<string>(type: "TEXT", nullable: false),
                    opening_float = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    closed_by = table.Column<string>(type: "TEXT", nullable: true),
                    closed_at = table.Column<string>(type: "TEXT", nullable: true),
                    counted_cash = table.Column<long>(type: "INTEGER", nullable: true),
                    expected_cash = table.Column<long>(type: "INTEGER", nullable: true),
                    variance = table.Column<long>(type: "INTEGER", nullable: true),
                    z_report_number = table.Column<long>(type: "INTEGER", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "open"),
                    notes = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_sessions", x => x.session_id);
                    table.CheckConstraint("ck_cash_sessions_closed_at", "closed_at IS NULL OR closed_at >= opened_at");
                    table.CheckConstraint("ck_cash_sessions_status", "status IN ('open','closed','suspended')");
                    table.CheckConstraint("ck_cash_sessions_status_2", "status <> 'closed' OR counted_cash IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "consent_events",
                columns: table => new
                {
                    consent_event_id = table.Column<string>(type: "TEXT", nullable: false),
                    customer_id = table.Column<string>(type: "TEXT", nullable: false),
                    occurred_at = table.Column<string>(type: "TEXT", nullable: false),
                    action = table.Column<string>(type: "TEXT", nullable: false),
                    consent_type = table.Column<string>(type: "TEXT", nullable: false),
                    notice_version = table.Column<string>(type: "TEXT", nullable: false),
                    captured_by = table.Column<string>(type: "TEXT", nullable: true),
                    method = table.Column<string>(type: "TEXT", nullable: false),
                    terminal_id = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consent_events", x => x.consent_event_id);
                    table.CheckConstraint("ck_consent_events_action", "action IN ('granted','withdrawn','renewed')");
                    table.CheckConstraint("ck_consent_events_consent_type", "consent_type IN ('processing','marketing')");
                    table.CheckConstraint("ck_consent_events_method", "method IN ('verbal','written','digital')");
                    table.ForeignKey(
                        name: "FK_consent_events_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "customer_id");
                    table.ForeignKey(
                        name: "FK_consent_events_notice_versions_notice_version",
                        column: x => x.notice_version,
                        principalTable: "notice_versions",
                        principalColumn: "version_code");
                });

            migrationBuilder.CreateTable(
                name: "credit_movements",
                columns: table => new
                {
                    movement_id = table.Column<string>(type: "TEXT", nullable: false),
                    customer_id = table.Column<string>(type: "TEXT", nullable: false),
                    movement_type = table.Column<string>(type: "TEXT", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    balance_after = table.Column<long>(type: "INTEGER", nullable: false),
                    transaction_id = table.Column<string>(type: "TEXT", nullable: true),
                    return_id = table.Column<string>(type: "TEXT", nullable: true),
                    expires_at = table.Column<string>(type: "TEXT", nullable: true),
                    staff_id = table.Column<string>(type: "TEXT", nullable: true),
                    terminal_id = table.Column<string>(type: "TEXT", nullable: true),
                    reason_code = table.Column<string>(type: "TEXT", nullable: true),
                    note = table.Column<string>(type: "TEXT", nullable: true),
                    occurred_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_movements", x => x.movement_id);
                    table.CheckConstraint("ck_credit_movements_amount", "amount <> 0");
                    table.CheckConstraint("ck_credit_movements_movement_type", "movement_type IN ('issue','redeem','adjust','expire','reverse')");
                    table.ForeignKey(
                        name: "FK_credit_movements_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "customer_id");
                    table.ForeignKey(
                        name: "FK_credit_movements_reason_codes_reason_code",
                        column: x => x.reason_code,
                        principalTable: "reason_codes",
                        principalColumn: "reason_code");
                });

            migrationBuilder.CreateTable(
                name: "data_subject_requests",
                columns: table => new
                {
                    request_id = table.Column<string>(type: "TEXT", nullable: false),
                    customer_id = table.Column<string>(type: "TEXT", nullable: false),
                    request_type = table.Column<string>(type: "TEXT", nullable: false),
                    received_at = table.Column<string>(type: "TEXT", nullable: false),
                    due_at = table.Column<string>(type: "TEXT", nullable: false),
                    received_by = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "open"),
                    resolution_note = table.Column<string>(type: "TEXT", nullable: true),
                    resolved_at = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_subject_requests", x => x.request_id);
                    table.CheckConstraint("ck_data_subject_requests_request_type", "request_type IN ('information','access','rectification','objection','erasure')");
                    table.CheckConstraint("ck_data_subject_requests_status", "status IN ('open','in_progress','fulfilled','refused','blocked')");
                    table.CheckConstraint("ck_data_subject_requests_status_2", "status NOT IN ('refused','blocked') OR resolution_note IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_data_subject_requests_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "customer_id");
                });

            migrationBuilder.CreateTable(
                name: "erasure_ledger",
                columns: table => new
                {
                    erasure_id = table.Column<string>(type: "TEXT", nullable: false),
                    subject_type = table.Column<string>(type: "TEXT", nullable: false),
                    subject_id = table.Column<string>(type: "TEXT", nullable: false),
                    request_id = table.Column<string>(type: "TEXT", nullable: true),
                    requested_at = table.Column<string>(type: "TEXT", nullable: false),
                    executed_at = table.Column<string>(type: "TEXT", nullable: true),
                    executed_by = table.Column<string>(type: "TEXT", nullable: true),
                    scope_json = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "pending"),
                    blocked_reason = table.Column<string>(type: "TEXT", nullable: true),
                    cloud_confirmed_at = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_erasure_ledger", x => x.erasure_id);
                    table.CheckConstraint("ck_erasure_ledger_status", "status IN ('pending','executed','blocked')");
                    table.CheckConstraint("ck_erasure_ledger_status_2", "status <> 'blocked' OR blocked_reason IS NOT NULL");
                    table.CheckConstraint("ck_erasure_ledger_status_3", "status <> 'executed' OR executed_at IS NOT NULL");
                    table.CheckConstraint("ck_erasure_ledger_subject_type", "subject_type IN ('customer','staff')");
                    table.ForeignKey(
                        name: "FK_erasure_ledger_data_subject_requests_request_id",
                        column: x => x.request_id,
                        principalTable: "data_subject_requests",
                        principalColumn: "request_id");
                });

            migrationBuilder.CreateTable(
                name: "intents",
                columns: table => new
                {
                    intent_id = table.Column<string>(type: "TEXT", nullable: false),
                    intent_type = table.Column<string>(type: "TEXT", nullable: false),
                    store_id = table.Column<string>(type: "TEXT", nullable: false),
                    payload_json = table.Column<string>(type: "TEXT", nullable: false),
                    preconditions_json = table.Column<string>(type: "TEXT", nullable: false),
                    created_at_cloud = table.Column<string>(type: "TEXT", nullable: false),
                    expires_at = table.Column<string>(type: "TEXT", nullable: true),
                    received_at = table.Column<string>(type: "TEXT", nullable: false),
                    evaluated_at = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "pending"),
                    rejection_reason = table.Column<string>(type: "TEXT", nullable: true),
                    fresh_request_id = table.Column<string>(type: "TEXT", nullable: true),
                    decided_by_cloud_user = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_intents", x => x.intent_id);
                    table.CheckConstraint("ck_intents_intent_type", "intent_type IN ('accept_reorder','markdown_stage','adjust_quantity','dismiss', 'price_change','promotion','catalogue_edit','supplier_edit', 'rights_action')");
                    table.CheckConstraint("ck_intents_status", "status IN ('pending','applied','rejected_stale','rejected_invalid')");
                    table.CheckConstraint("ck_intents_status_2", "status NOT IN ('rejected_stale','rejected_invalid') OR rejection_reason IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "inventories",
                columns: table => new
                {
                    store_id = table.Column<string>(type: "TEXT", nullable: false),
                    variant_id = table.Column<string>(type: "TEXT", nullable: false),
                    batch_id = table.Column<string>(type: "TEXT", nullable: false),
                    quantity = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    reserved_quantity = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventories", x => new { x.store_id, x.variant_id, x.batch_id });
                    table.CheckConstraint("ck_inventories_reserved_quantity", "reserved_quantity >= 0");
                    table.ForeignKey(
                        name: "FK_inventories_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "batch_id");
                    table.ForeignKey(
                        name: "FK_inventories_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "variants",
                        principalColumn: "variant_id");
                });

            migrationBuilder.CreateTable(
                name: "loyalty_movements",
                columns: table => new
                {
                    movement_id = table.Column<string>(type: "TEXT", nullable: false),
                    customer_id = table.Column<string>(type: "TEXT", nullable: false),
                    movement_type = table.Column<string>(type: "TEXT", nullable: false),
                    points = table.Column<long>(type: "INTEGER", nullable: false),
                    balance_after = table.Column<long>(type: "INTEGER", nullable: false),
                    transaction_id = table.Column<string>(type: "TEXT", nullable: true),
                    staff_id = table.Column<string>(type: "TEXT", nullable: true),
                    terminal_id = table.Column<string>(type: "TEXT", nullable: true),
                    reason_code = table.Column<string>(type: "TEXT", nullable: true),
                    note = table.Column<string>(type: "TEXT", nullable: true),
                    occurred_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loyalty_movements", x => x.movement_id);
                    table.CheckConstraint("ck_loyalty_movements_movement_type", "movement_type IN ('earn','redeem','adjust','expire','reverse')");
                    table.CheckConstraint("ck_loyalty_movements_points", "points <> 0");
                    table.ForeignKey(
                        name: "FK_loyalty_movements_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "customer_id");
                    table.ForeignKey(
                        name: "FK_loyalty_movements_reason_codes_reason_code",
                        column: x => x.reason_code,
                        principalTable: "reason_codes",
                        principalColumn: "reason_code");
                });

            migrationBuilder.CreateTable(
                name: "prices",
                columns: table => new
                {
                    variant_id = table.Column<string>(type: "TEXT", nullable: false),
                    valid_from = table.Column<string>(type: "TEXT", nullable: false),
                    store_id = table.Column<string>(type: "TEXT", nullable: false),
                    price_type = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "retail"),
                    price = table.Column<long>(type: "INTEGER", nullable: false),
                    currency = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "DZD"),
                    is_tax_inclusive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    valid_to = table.Column<string>(type: "TEXT", nullable: true),
                    created_by = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prices", x => new { x.variant_id, x.valid_from, x.store_id, x.price_type });
                    table.CheckConstraint("ck_prices_is_tax_inclusive", "is_tax_inclusive IN (0,1)");
                    table.CheckConstraint("ck_prices_price", "price >= 0");
                    table.CheckConstraint("ck_prices_price_type", "price_type IN ('retail','wholesale','staff','promotional')");
                    table.CheckConstraint("ck_prices_valid_to", "valid_to IS NULL OR valid_to > valid_from");
                    table.ForeignKey(
                        name: "FK_prices_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "variants",
                        principalColumn: "variant_id");
                });

            migrationBuilder.CreateTable(
                name: "processing_log",
                columns: table => new
                {
                    log_id = table.Column<string>(type: "TEXT", nullable: false),
                    occurred_at = table.Column<string>(type: "TEXT", nullable: false),
                    operation = table.Column<string>(type: "TEXT", nullable: false),
                    subject_type = table.Column<string>(type: "TEXT", nullable: false),
                    subject_id = table.Column<string>(type: "TEXT", nullable: true),
                    actor_type = table.Column<string>(type: "TEXT", nullable: false),
                    actor_id = table.Column<string>(type: "TEXT", nullable: true),
                    purpose = table.Column<string>(type: "TEXT", nullable: false),
                    recipient = table.Column<string>(type: "TEXT", nullable: true),
                    source_module = table.Column<string>(type: "TEXT", nullable: false),
                    store_id = table.Column<string>(type: "TEXT", nullable: true),
                    terminal_id = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processing_log", x => x.log_id);
                    table.CheckConstraint("ck_processing_log_actor_type", "actor_type IN ('staff','system','engine')");
                    table.CheckConstraint("ck_processing_log_operation", "operation IN ('collection','consultation','disclosure','transmission', 'modification','erasure','pseudonymisation','re_identification')");
                    table.CheckConstraint("ck_processing_log_subject_type", "subject_type IN ('customer','staff')");
                });

            migrationBuilder.CreateTable(
                name: "promotion_product",
                columns: table => new
                {
                    promotion_id = table.Column<string>(type: "TEXT", nullable: false),
                    product_id = table.Column<string>(type: "TEXT", nullable: false),
                    valid_from = table.Column<string>(type: "TEXT", nullable: false),
                    valid_to = table.Column<string>(type: "TEXT", nullable: true),
                    value_type = table.Column<string>(type: "TEXT", nullable: false),
                    promotion_value = table.Column<long>(type: "INTEGER", nullable: false),
                    min_quantity = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    max_redemptions = table.Column<long>(type: "INTEGER", nullable: true),
                    redemption_count = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    priority = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 100L),
                    is_stackable = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "active")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_product", x => new { x.promotion_id, x.product_id, x.valid_from });
                    table.CheckConstraint("ck_promotion_product_is_stackable", "is_stackable IN (0,1)");
                    table.CheckConstraint("ck_promotion_product_promotion_value", "promotion_value >= 0");
                    table.CheckConstraint("ck_promotion_product_status", "status IN ('active','paused','ended')");
                    table.CheckConstraint("ck_promotion_product_valid_to", "valid_to IS NULL OR valid_to > valid_from");
                    table.CheckConstraint("ck_promotion_product_value_type", "value_type IN ('percent','amount','bogo')");
                    table.CheckConstraint("ck_promotion_product_value_type_2", "value_type <> 'percent' OR promotion_value <= 10000");
                    table.ForeignKey(
                        name: "FK_promotion_product_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "product_id");
                });

            migrationBuilder.CreateTable(
                name: "promotion_variant",
                columns: table => new
                {
                    promotion_id = table.Column<string>(type: "TEXT", nullable: false),
                    variant_id = table.Column<string>(type: "TEXT", nullable: false),
                    valid_from = table.Column<string>(type: "TEXT", nullable: false),
                    valid_to = table.Column<string>(type: "TEXT", nullable: true),
                    value_type = table.Column<string>(type: "TEXT", nullable: false),
                    promotion_value = table.Column<long>(type: "INTEGER", nullable: false),
                    min_quantity = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    max_redemptions = table.Column<long>(type: "INTEGER", nullable: true),
                    redemption_count = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    priority = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 100L),
                    is_stackable = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "active")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_variant", x => new { x.promotion_id, x.variant_id, x.valid_from });
                    table.CheckConstraint("ck_promotion_variant_is_stackable", "is_stackable IN (0,1)");
                    table.CheckConstraint("ck_promotion_variant_promotion_value", "promotion_value >= 0");
                    table.CheckConstraint("ck_promotion_variant_status", "status IN ('active','paused','ended')");
                    table.CheckConstraint("ck_promotion_variant_valid_to", "valid_to IS NULL OR valid_to > valid_from");
                    table.CheckConstraint("ck_promotion_variant_value_type", "value_type IN ('percent','amount','bogo')");
                    table.CheckConstraint("ck_promotion_variant_value_type_2", "value_type <> 'percent' OR promotion_value <= 10000");
                    table.ForeignKey(
                        name: "FK_promotion_variant_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "variants",
                        principalColumn: "variant_id");
                });

            migrationBuilder.CreateTable(
                name: "promotions",
                columns: table => new
                {
                    promotion_id = table.Column<string>(type: "TEXT", nullable: false),
                    promotion_name = table.Column<string>(type: "TEXT", nullable: false),
                    promotion_type = table.Column<string>(type: "TEXT", nullable: false),
                    store_id = table.Column<string>(type: "TEXT", nullable: true),
                    source_recommendation_id = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "draft"),
                    created_by = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotions", x => x.promotion_id);
                    table.CheckConstraint("ck_promotions_promotion_type", "promotion_type IN ('discount','bogo','bundle','markdown')");
                    table.CheckConstraint("ck_promotions_status", "status IN ('draft','scheduled','active','ended','cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_items",
                columns: table => new
                {
                    order_id = table.Column<string>(type: "TEXT", nullable: false),
                    variant_id = table.Column<string>(type: "TEXT", nullable: false),
                    quantity_ordered = table.Column<long>(type: "INTEGER", nullable: false),
                    quantity_received = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    purchase_unit_code = table.Column<string>(type: "TEXT", nullable: false),
                    units_per_purchase_unit = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 1000L),
                    unit_cost = table.Column<long>(type: "INTEGER", nullable: false),
                    discount = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    line_total = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_order_items", x => new { x.order_id, x.variant_id });
                    table.CheckConstraint("ck_purchase_order_items_discount", "discount >= 0");
                    table.CheckConstraint("ck_purchase_order_items_line_total", "line_total >= 0");
                    table.CheckConstraint("ck_purchase_order_items_quantity_ordered", "quantity_ordered > 0");
                    table.CheckConstraint("ck_purchase_order_items_quantity_received", "quantity_received >= 0");
                    table.CheckConstraint("ck_purchase_order_items_unit_cost", "unit_cost >= 0");
                    table.ForeignKey(
                        name: "FK_purchase_order_items_units_of_measure_purchase_unit_code",
                        column: x => x.purchase_unit_code,
                        principalTable: "units_of_measure",
                        principalColumn: "unit_code");
                    table.ForeignKey(
                        name: "FK_purchase_order_items_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "variants",
                        principalColumn: "variant_id");
                });

            migrationBuilder.CreateTable(
                name: "purchase_orders",
                columns: table => new
                {
                    order_id = table.Column<string>(type: "TEXT", nullable: false),
                    store_id = table.Column<string>(type: "TEXT", nullable: false),
                    supplier_id = table.Column<string>(type: "TEXT", nullable: false),
                    order_date = table.Column<string>(type: "TEXT", nullable: false),
                    expected_arrival_date = table.Column<string>(type: "TEXT", nullable: true),
                    received_at = table.Column<string>(type: "TEXT", nullable: true),
                    total_amount = table.Column<long>(type: "INTEGER", nullable: true),
                    currency = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "DZD"),
                    source = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "manual"),
                    source_recommendation_id = table.Column<string>(type: "TEXT", nullable: true),
                    created_by = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "draft"),
                    notes = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_orders", x => x.order_id);
                    table.CheckConstraint("ck_purchase_orders_source", "source IN ('manual','recommendation','reorder_rule')");
                    table.CheckConstraint("ck_purchase_orders_status", "status IN ('draft','sent','partially_received','received','cancelled')");
                    table.ForeignKey(
                        name: "FK_purchase_orders_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "supplier_id");
                });

            migrationBuilder.CreateTable(
                name: "recommendation_decisions",
                columns: table => new
                {
                    decision_id = table.Column<string>(type: "TEXT", nullable: false),
                    recommendation_id = table.Column<string>(type: "TEXT", nullable: false),
                    decision = table.Column<string>(type: "TEXT", nullable: false),
                    chosen_option_id = table.Column<string>(type: "TEXT", nullable: true),
                    adjusted_payload_json = table.Column<string>(type: "TEXT", nullable: true),
                    snooze_until = table.Column<string>(type: "TEXT", nullable: true),
                    origin = table.Column<string>(type: "TEXT", nullable: false),
                    decided_by = table.Column<string>(type: "TEXT", nullable: true),
                    decided_at = table.Column<string>(type: "TEXT", nullable: false),
                    terminal_id = table.Column<string>(type: "TEXT", nullable: true),
                    applied_at = table.Column<string>(type: "TEXT", nullable: true),
                    resulting_entity_type = table.Column<string>(type: "TEXT", nullable: true),
                    resulting_entity_id = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendation_decisions", x => x.decision_id);
                    table.CheckConstraint("ck_recommendation_decisions_decision", "decision IN ('accept','adjust','dismiss','snooze')");
                    table.CheckConstraint("ck_recommendation_decisions_decision_2", "decision <> 'snooze' OR snooze_until IS NOT NULL");
                    table.CheckConstraint("ck_recommendation_decisions_decision_3", "decision <> 'adjust' OR adjusted_payload_json IS NOT NULL");
                    table.CheckConstraint("ck_recommendation_decisions_origin", "origin IN ('store','cloud')");
                });

            migrationBuilder.CreateTable(
                name: "recommendation_options",
                columns: table => new
                {
                    option_id = table.Column<string>(type: "TEXT", nullable: false),
                    recommendation_id = table.Column<string>(type: "TEXT", nullable: false),
                    label = table.Column<string>(type: "TEXT", nullable: false),
                    display_order = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    payload_json = table.Column<string>(type: "TEXT", nullable: false),
                    projected_value = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendation_options", x => x.option_id);
                });

            migrationBuilder.CreateTable(
                name: "recommendations",
                columns: table => new
                {
                    recommendation_id = table.Column<string>(type: "TEXT", nullable: false),
                    store_id = table.Column<string>(type: "TEXT", nullable: false),
                    department = table.Column<string>(type: "TEXT", nullable: false),
                    urgency = table.Column<string>(type: "TEXT", nullable: false),
                    action_type = table.Column<string>(type: "TEXT", nullable: false),
                    subject_type = table.Column<string>(type: "TEXT", nullable: false),
                    subject_id = table.Column<string>(type: "TEXT", nullable: false),
                    headline = table.Column<string>(type: "TEXT", nullable: false),
                    because_json = table.Column<string>(type: "TEXT", nullable: false),
                    interval_low = table.Column<long>(type: "INTEGER", nullable: true),
                    interval_high = table.Column<long>(type: "INTEGER", nullable: true),
                    computed_at = table.Column<string>(type: "TEXT", nullable: false),
                    parameter_version = table.Column<long>(type: "INTEGER", nullable: true),
                    source = table.Column<string>(type: "TEXT", nullable: false),
                    minimum_required_role = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "pending"),
                    issued_at = table.Column<string>(type: "TEXT", nullable: false),
                    delivered_at = table.Column<string>(type: "TEXT", nullable: true),
                    expires_at = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendations", x => x.recommendation_id);
                    table.CheckConstraint("ck_recommendations_action_type", "action_type IN ('binary','menu')");
                    table.CheckConstraint("ck_recommendations_department", "department IN ('inventory','sales_demand','supply','planning','customer')");
                    table.CheckConstraint("ck_recommendations_interval_low", "interval_low IS NULL OR interval_high IS NULL OR interval_high >= interval_low");
                    table.CheckConstraint("ck_recommendations_status", "status IN ('pending','delivered','decided','expired','superseded')");
                    table.CheckConstraint("ck_recommendations_subject_type", "subject_type IN ('variant','batch','product','supplier','customer','store')");
                    table.CheckConstraint("ck_recommendations_urgency", "urgency IN ('quiet','standard','warning','critical')");
                    table.ForeignKey(
                        name: "FK_recommendations_roles_minimum_required_role",
                        column: x => x.minimum_required_role,
                        principalTable: "roles",
                        principalColumn: "role_code");
                });

            migrationBuilder.CreateTable(
                name: "returns",
                columns: table => new
                {
                    return_id = table.Column<string>(type: "TEXT", nullable: false),
                    transaction_item_id = table.Column<string>(type: "TEXT", nullable: false),
                    store_id = table.Column<string>(type: "TEXT", nullable: false),
                    terminal_id = table.Column<string>(type: "TEXT", nullable: true),
                    batch_id = table.Column<string>(type: "TEXT", nullable: true),
                    staff_id = table.Column<string>(type: "TEXT", nullable: false),
                    approved_by = table.Column<string>(type: "TEXT", nullable: true),
                    quantity_returned = table.Column<long>(type: "INTEGER", nullable: false),
                    refund_amount = table.Column<long>(type: "INTEGER", nullable: false),
                    refund_method = table.Column<string>(type: "TEXT", nullable: false),
                    restock_flag = table.Column<bool>(type: "INTEGER", nullable: false),
                    reason_code = table.Column<string>(type: "TEXT", nullable: false),
                    note = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_returns", x => x.return_id);
                    table.CheckConstraint("ck_returns_quantity_returned", "quantity_returned > 0");
                    table.CheckConstraint("ck_returns_refund_amount", "refund_amount >= 0");
                    table.CheckConstraint("ck_returns_refund_method", "refund_method IN ('cash','card','store_credit','exchange')");
                    table.CheckConstraint("ck_returns_restock_flag", "restock_flag IN (0,1)");
                    table.ForeignKey(
                        name: "FK_returns_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "batch_id");
                    table.ForeignKey(
                        name: "FK_returns_reason_codes_reason_code",
                        column: x => x.reason_code,
                        principalTable: "reason_codes",
                        principalColumn: "reason_code");
                });

            migrationBuilder.CreateTable(
                name: "shifts",
                columns: table => new
                {
                    shift_id = table.Column<string>(type: "TEXT", nullable: false),
                    staff_id = table.Column<string>(type: "TEXT", nullable: false),
                    terminal_id = table.Column<string>(type: "TEXT", nullable: true),
                    store_id = table.Column<string>(type: "TEXT", nullable: false),
                    start_time = table.Column<string>(type: "TEXT", nullable: false),
                    end_time = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "open"),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shifts", x => x.shift_id);
                    table.CheckConstraint("ck_shifts_end_time", "end_time IS NULL OR end_time >= start_time");
                    table.CheckConstraint("ck_shifts_status", "status IN ('open','closed')");
                });

            migrationBuilder.CreateTable(
                name: "staff",
                columns: table => new
                {
                    staff_id = table.Column<string>(type: "TEXT", nullable: false),
                    store_id = table.Column<string>(type: "TEXT", nullable: false),
                    staff_name = table.Column<string>(type: "TEXT", nullable: false),
                    contact_phone = table.Column<string>(type: "TEXT", nullable: true),
                    email = table.Column<string>(type: "TEXT", nullable: true),
                    role = table.Column<string>(type: "TEXT", nullable: false),
                    pin_hash = table.Column<string>(type: "TEXT", nullable: false),
                    join_date = table.Column<string>(type: "TEXT", nullable: false),
                    last_seen_date = table.Column<string>(type: "TEXT", nullable: true),
                    termination_date = table.Column<string>(type: "TEXT", nullable: true),
                    notice_version_acknowledged = table.Column<string>(type: "TEXT", nullable: true),
                    notice_acknowledged_at = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "active"),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff", x => x.staff_id);
                    table.CheckConstraint("ck_staff_status", "status IN ('active','suspended','terminated')");
                    table.ForeignKey(
                        name: "FK_staff_notice_versions_notice_version_acknowledged",
                        column: x => x.notice_version_acknowledged,
                        principalTable: "notice_versions",
                        principalColumn: "version_code");
                    table.ForeignKey(
                        name: "FK_staff_roles_role",
                        column: x => x.role,
                        principalTable: "roles",
                        principalColumn: "role_code");
                });

            migrationBuilder.CreateTable(
                name: "stores",
                columns: table => new
                {
                    store_id = table.Column<string>(type: "TEXT", nullable: false),
                    store_code = table.Column<string>(type: "TEXT", nullable: false),
                    store_name = table.Column<string>(type: "TEXT", nullable: false),
                    contact_phone = table.Column<string>(type: "TEXT", nullable: true),
                    email = table.Column<string>(type: "TEXT", nullable: true),
                    address = table.Column<string>(type: "TEXT", nullable: true),
                    latitude = table.Column<string>(type: "TEXT", nullable: true),
                    longitude = table.Column<string>(type: "TEXT", nullable: true),
                    store_type = table.Column<string>(type: "TEXT", nullable: false),
                    currency = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "DZD"),
                    timezone = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Africa/Algiers"),
                    tax_registration_number = table.Column<string>(type: "TEXT", nullable: true),
                    fiscal_year_start = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "01-01"),
                    manager_id = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "active"),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stores", x => x.store_id);
                    table.CheckConstraint("ck_stores_status", "status IN ('active','suspended','closed')");
                    table.ForeignKey(
                        name: "FK_stores_staff_manager_id",
                        column: x => x.manager_id,
                        principalTable: "staff",
                        principalColumn: "staff_id");
                });

            migrationBuilder.CreateTable(
                name: "stock_counts",
                columns: table => new
                {
                    count_id = table.Column<string>(type: "TEXT", nullable: false),
                    store_id = table.Column<string>(type: "TEXT", nullable: false),
                    count_type = table.Column<string>(type: "TEXT", nullable: false),
                    scope_category_id = table.Column<string>(type: "TEXT", nullable: true),
                    started_by = table.Column<string>(type: "TEXT", nullable: false),
                    started_at = table.Column<string>(type: "TEXT", nullable: false),
                    completed_at = table.Column<string>(type: "TEXT", nullable: true),
                    approved_by = table.Column<string>(type: "TEXT", nullable: true),
                    approved_at = table.Column<string>(type: "TEXT", nullable: true),
                    total_variance_value = table.Column<long>(type: "INTEGER", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "draft"),
                    notes = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_counts", x => x.count_id);
                    table.CheckConstraint("ck_stock_counts_count_type", "count_type IN ('full','cycle','spot')");
                    table.CheckConstraint("ck_stock_counts_status", "status IN ('draft','counting','review','posted','cancelled')");
                    table.CheckConstraint("ck_stock_counts_status_2", "status <> 'posted' OR approved_by IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_stock_counts_categories_scope_category_id",
                        column: x => x.scope_category_id,
                        principalTable: "categories",
                        principalColumn: "category_id");
                    table.ForeignKey(
                        name: "FK_stock_counts_staff_approved_by",
                        column: x => x.approved_by,
                        principalTable: "staff",
                        principalColumn: "staff_id");
                    table.ForeignKey(
                        name: "FK_stock_counts_staff_started_by",
                        column: x => x.started_by,
                        principalTable: "staff",
                        principalColumn: "staff_id");
                    table.ForeignKey(
                        name: "FK_stock_counts_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "store_id");
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                columns: table => new
                {
                    movement_id = table.Column<string>(type: "TEXT", nullable: false),
                    store_id = table.Column<string>(type: "TEXT", nullable: false),
                    variant_id = table.Column<string>(type: "TEXT", nullable: false),
                    batch_id = table.Column<string>(type: "TEXT", nullable: false),
                    movement_date = table.Column<string>(type: "TEXT", nullable: false),
                    movement_type = table.Column<string>(type: "TEXT", nullable: false),
                    quantity_changed = table.Column<long>(type: "INTEGER", nullable: false),
                    unit_code = table.Column<string>(type: "TEXT", nullable: false),
                    unit_cost = table.Column<long>(type: "INTEGER", nullable: true),
                    reference_type = table.Column<string>(type: "TEXT", nullable: true),
                    reference_id = table.Column<string>(type: "TEXT", nullable: true),
                    reason_code = table.Column<string>(type: "TEXT", nullable: true),
                    staff_id = table.Column<string>(type: "TEXT", nullable: true),
                    note = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_movements", x => x.movement_id);
                    table.CheckConstraint("ck_stock_movements_movement_type", "movement_type IN ('receipt','sale','return_in','return_out', 'adjustment','count','write_off','expiry','transfer')");
                    table.CheckConstraint("ck_stock_movements_quantity_changed", "quantity_changed <> 0");
                    table.CheckConstraint("ck_stock_movements_reference_type", "reference_type IS NULL OR reference_type IN ('transaction','purchase_order','stock_count','return','manual')");
                    table.ForeignKey(
                        name: "FK_stock_movements_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "batch_id");
                    table.ForeignKey(
                        name: "FK_stock_movements_reason_codes_reason_code",
                        column: x => x.reason_code,
                        principalTable: "reason_codes",
                        principalColumn: "reason_code");
                    table.ForeignKey(
                        name: "FK_stock_movements_staff_staff_id",
                        column: x => x.staff_id,
                        principalTable: "staff",
                        principalColumn: "staff_id");
                    table.ForeignKey(
                        name: "FK_stock_movements_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "store_id");
                    table.ForeignKey(
                        name: "FK_stock_movements_units_of_measure_unit_code",
                        column: x => x.unit_code,
                        principalTable: "units_of_measure",
                        principalColumn: "unit_code");
                    table.ForeignKey(
                        name: "FK_stock_movements_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "variants",
                        principalColumn: "variant_id");
                });

            migrationBuilder.CreateTable(
                name: "terminals",
                columns: table => new
                {
                    terminal_id = table.Column<string>(type: "TEXT", nullable: false),
                    store_id = table.Column<string>(type: "TEXT", nullable: false),
                    terminal_name = table.Column<string>(type: "TEXT", nullable: false),
                    is_replica = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    printer_port = table.Column<string>(type: "TEXT", nullable: true),
                    hardware_notes = table.Column<string>(type: "TEXT", nullable: true),
                    last_seen_at = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "active"),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_terminals", x => x.terminal_id);
                    table.CheckConstraint("ck_terminals_is_replica", "is_replica IN (0,1)");
                    table.CheckConstraint("ck_terminals_status", "status IN ('active','inactive','retired')");
                    table.ForeignKey(
                        name: "FK_terminals_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "store_id");
                });

            migrationBuilder.CreateTable(
                name: "stock_count_items",
                columns: table => new
                {
                    count_item_id = table.Column<string>(type: "TEXT", nullable: false),
                    count_id = table.Column<string>(type: "TEXT", nullable: false),
                    variant_id = table.Column<string>(type: "TEXT", nullable: false),
                    batch_id = table.Column<string>(type: "TEXT", nullable: false),
                    expected_quantity = table.Column<long>(type: "INTEGER", nullable: false),
                    counted_quantity = table.Column<long>(type: "INTEGER", nullable: true),
                    variance_quantity = table.Column<long>(type: "INTEGER", nullable: true),
                    unit_cost = table.Column<long>(type: "INTEGER", nullable: true),
                    variance_value = table.Column<long>(type: "INTEGER", nullable: true),
                    counted_by = table.Column<string>(type: "TEXT", nullable: true),
                    counted_at = table.Column<string>(type: "TEXT", nullable: true),
                    recount_flag = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    note = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_count_items", x => x.count_item_id);
                    table.CheckConstraint("ck_stock_count_items_recount_flag", "recount_flag IN (0,1)");
                    table.ForeignKey(
                        name: "FK_stock_count_items_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "batch_id");
                    table.ForeignKey(
                        name: "FK_stock_count_items_staff_counted_by",
                        column: x => x.counted_by,
                        principalTable: "staff",
                        principalColumn: "staff_id");
                    table.ForeignKey(
                        name: "FK_stock_count_items_stock_counts_count_id",
                        column: x => x.count_id,
                        principalTable: "stock_counts",
                        principalColumn: "count_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_stock_count_items_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "variants",
                        principalColumn: "variant_id");
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                columns: table => new
                {
                    transaction_id = table.Column<string>(type: "TEXT", nullable: false),
                    store_id = table.Column<string>(type: "TEXT", nullable: false),
                    terminal_id = table.Column<string>(type: "TEXT", nullable: false),
                    cash_session_id = table.Column<string>(type: "TEXT", nullable: true),
                    staff_id = table.Column<string>(type: "TEXT", nullable: false),
                    customer_id = table.Column<string>(type: "TEXT", nullable: true),
                    invoice_number = table.Column<string>(type: "TEXT", nullable: true),
                    occurred_at = table.Column<string>(type: "TEXT", nullable: false),
                    subtotal = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    discount_total = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    tax_total = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    total_amount = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    currency = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "DZD"),
                    ecommerce_flag = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    original_transaction_id = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "open"),
                    voided_at = table.Column<string>(type: "TEXT", nullable: true),
                    voided_by = table.Column<string>(type: "TEXT", nullable: true),
                    void_reason_code = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transactions", x => x.transaction_id);
                    table.CheckConstraint("ck_transactions_ecommerce_flag", "ecommerce_flag IN (0,1)");
                    table.CheckConstraint("ck_transactions_status", "status IN ('open','parked','completed','voided','refunded','partially_refunded')");
                    table.CheckConstraint("ck_transactions_status_2", "status <> 'voided' OR (voided_at IS NOT NULL AND void_reason_code IS NOT NULL)");
                    table.CheckConstraint("ck_transactions_status_3", "status <> 'completed' OR invoice_number IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_transactions_cash_sessions_cash_session_id",
                        column: x => x.cash_session_id,
                        principalTable: "cash_sessions",
                        principalColumn: "session_id");
                    table.ForeignKey(
                        name: "FK_transactions_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "customer_id");
                    table.ForeignKey(
                        name: "FK_transactions_reason_codes_void_reason_code",
                        column: x => x.void_reason_code,
                        principalTable: "reason_codes",
                        principalColumn: "reason_code");
                    table.ForeignKey(
                        name: "FK_transactions_staff_staff_id",
                        column: x => x.staff_id,
                        principalTable: "staff",
                        principalColumn: "staff_id");
                    table.ForeignKey(
                        name: "FK_transactions_staff_voided_by",
                        column: x => x.voided_by,
                        principalTable: "staff",
                        principalColumn: "staff_id");
                    table.ForeignKey(
                        name: "FK_transactions_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "store_id");
                    table.ForeignKey(
                        name: "FK_transactions_terminals_terminal_id",
                        column: x => x.terminal_id,
                        principalTable: "terminals",
                        principalColumn: "terminal_id");
                    table.ForeignKey(
                        name: "FK_transactions_transactions_original_transaction_id",
                        column: x => x.original_transaction_id,
                        principalTable: "transactions",
                        principalColumn: "transaction_id");
                });

            migrationBuilder.CreateTable(
                name: "transaction_items",
                columns: table => new
                {
                    transaction_item_id = table.Column<string>(type: "TEXT", nullable: false),
                    transaction_id = table.Column<string>(type: "TEXT", nullable: false),
                    variant_id = table.Column<string>(type: "TEXT", nullable: false),
                    batch_id = table.Column<string>(type: "TEXT", nullable: true),
                    promotion_id = table.Column<string>(type: "TEXT", nullable: true),
                    quantity = table.Column<long>(type: "INTEGER", nullable: false),
                    unit_code = table.Column<string>(type: "TEXT", nullable: false),
                    sell_price = table.Column<long>(type: "INTEGER", nullable: false),
                    unit_cost_at_sale = table.Column<long>(type: "INTEGER", nullable: true),
                    discount_amount = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    discount_reason_code = table.Column<string>(type: "TEXT", nullable: true),
                    authorised_by = table.Column<string>(type: "TEXT", nullable: true),
                    tax_amount = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    line_total = table.Column<long>(type: "INTEGER", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transaction_items", x => x.transaction_item_id);
                    table.CheckConstraint("ck_transaction_items_discount_amount", "discount_amount >= 0");
                    table.CheckConstraint("ck_transaction_items_discount_amount_2", "discount_amount = 0 OR discount_reason_code IS NOT NULL");
                    table.CheckConstraint("ck_transaction_items_quantity", "quantity <> 0");
                    table.CheckConstraint("ck_transaction_items_sell_price", "sell_price >= 0");
                    table.ForeignKey(
                        name: "FK_transaction_items_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "batch_id");
                    table.ForeignKey(
                        name: "FK_transaction_items_promotions_promotion_id",
                        column: x => x.promotion_id,
                        principalTable: "promotions",
                        principalColumn: "promotion_id");
                    table.ForeignKey(
                        name: "FK_transaction_items_reason_codes_discount_reason_code",
                        column: x => x.discount_reason_code,
                        principalTable: "reason_codes",
                        principalColumn: "reason_code");
                    table.ForeignKey(
                        name: "FK_transaction_items_staff_authorised_by",
                        column: x => x.authorised_by,
                        principalTable: "staff",
                        principalColumn: "staff_id");
                    table.ForeignKey(
                        name: "FK_transaction_items_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalTable: "transactions",
                        principalColumn: "transaction_id");
                    table.ForeignKey(
                        name: "FK_transaction_items_units_of_measure_unit_code",
                        column: x => x.unit_code,
                        principalTable: "units_of_measure",
                        principalColumn: "unit_code");
                    table.ForeignKey(
                        name: "FK_transaction_items_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "variants",
                        principalColumn: "variant_id");
                });

            migrationBuilder.CreateTable(
                name: "transaction_payments",
                columns: table => new
                {
                    payment_id = table.Column<string>(type: "TEXT", nullable: false),
                    transaction_id = table.Column<string>(type: "TEXT", nullable: false),
                    sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    payment_method = table.Column<string>(type: "TEXT", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    currency = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "DZD"),
                    reference = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transaction_payments", x => x.payment_id);
                    table.CheckConstraint("ck_transaction_payments_amount", "amount <> 0");
                    table.CheckConstraint("ck_transaction_payments_payment_method", "payment_method IN ('cash','card','mobile_wallet','store_credit','on_account')");
                    table.ForeignKey(
                        name: "FK_transaction_payments_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalTable: "transactions",
                        principalColumn: "transaction_id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_batch_items_variant",
                table: "batch_items",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "ix_batches_expiry",
                table: "batches",
                columns: new[] { "store_id", "expiration_date" },
                filter: "expiration_date IS NOT NULL AND status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_batches_order",
                table: "batches",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_batches_product",
                table: "batches",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_cash_movements_session",
                table: "cash_movements",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_cash_sessions_open",
                table: "cash_sessions",
                column: "store_id",
                filter: "status = 'open'");

            migrationBuilder.CreateIndex(
                name: "ix_cash_sessions_terminal",
                table: "cash_sessions",
                columns: new[] { "terminal_id", "opened_at" });

            migrationBuilder.CreateIndex(
                name: "IX_categories_slug",
                table: "categories",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_categories_parent",
                table: "categories",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_category_attr_attribute",
                table: "category_attributes",
                column: "attribute_code");

            migrationBuilder.CreateIndex(
                name: "ix_consent_customer",
                table: "consent_events",
                columns: new[] { "customer_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_credit_customer",
                table: "credit_movements",
                columns: new[] { "customer_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_customers_phone",
                table: "customers",
                column: "contact_phone");

            migrationBuilder.CreateIndex(
                name: "ix_dsr_customer",
                table: "data_subject_requests",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_dsr_open",
                table: "data_subject_requests",
                column: "due_at",
                filter: "status IN ('open','in_progress')");

            migrationBuilder.CreateIndex(
                name: "ix_erasure_subject",
                table: "erasure_ledger",
                columns: new[] { "subject_type", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "IX_inbox_message_id",
                table: "inbox",
                column: "message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbox_pending",
                table: "inbox",
                columns: new[] { "status", "cloud_sequence" },
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_intents_expiry",
                table: "intents",
                column: "expires_at",
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_intents_pending",
                table: "intents",
                columns: new[] { "store_id", "status" },
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_inventories_batch",
                table: "inventories",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventories_variant",
                table: "inventories",
                columns: new[] { "variant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_loyalty_customer",
                table: "loyalty_movements",
                columns: new[] { "customer_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_sequence_number",
                table: "outbox",
                column: "sequence_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_drain",
                table: "outbox",
                columns: new[] { "is_priority", "sequence_number" });

            migrationBuilder.CreateIndex(
                name: "ux_parameter_current",
                table: "parameter_registry",
                columns: new[] { "parameter_code", "scope_type", "scope_id" },
                unique: true,
                filter: "is_current = 1");

            migrationBuilder.CreateIndex(
                name: "ix_prices_lookup",
                table: "prices",
                columns: new[] { "variant_id", "store_id", "valid_from" });

            migrationBuilder.CreateIndex(
                name: "ix_proclog_occurred",
                table: "processing_log",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_proclog_subject",
                table: "processing_log",
                columns: new[] { "subject_type", "subject_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pav_attribute",
                table: "product_attribute_values",
                column: "attribute_code");

            migrationBuilder.CreateIndex(
                name: "ix_bundle_items_variant",
                table: "product_bundle_items",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "IX_product_bundles_barcode",
                table: "product_bundles",
                column: "barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_category_cat",
                table: "product_category",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ux_product_category_primary",
                table: "product_category",
                column: "product_id",
                unique: true,
                filter: "is_primary = 1");

            migrationBuilder.CreateIndex(
                name: "ix_promo_product_product",
                table: "promotion_product",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_promo_variant_variant",
                table: "promotion_variant",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "ix_promotions_status",
                table: "promotions",
                columns: new[] { "status", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_po_items_variant",
                table: "purchase_order_items",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "ix_po_store_status",
                table: "purchase_orders",
                columns: new[] { "store_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_po_supplier",
                table: "purchase_orders",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_rec_decisions_date",
                table: "recommendation_decisions",
                column: "decided_at");

            migrationBuilder.CreateIndex(
                name: "ix_rec_decisions_rec",
                table: "recommendation_decisions",
                column: "recommendation_id");

            migrationBuilder.CreateIndex(
                name: "IX_recommendation_options_recommendation_id_display_order",
                table: "recommendation_options",
                columns: new[] { "recommendation_id", "display_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rec_options_rec",
                table: "recommendation_options",
                column: "recommendation_id");

            migrationBuilder.CreateIndex(
                name: "ix_recs_pending",
                table: "recommendations",
                columns: new[] { "store_id", "status", "urgency" },
                filter: "status IN ('pending','delivered')");

            migrationBuilder.CreateIndex(
                name: "ix_recs_subject",
                table: "recommendations",
                columns: new[] { "subject_type", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ix_returns_item",
                table: "returns",
                column: "transaction_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_returns_store_date",
                table: "returns",
                columns: new[] { "store_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_roles_rank",
                table: "roles",
                column: "rank",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shifts_staff",
                table: "shifts",
                columns: new[] { "staff_id", "start_time" });

            migrationBuilder.CreateIndex(
                name: "ix_shifts_store_open",
                table: "shifts",
                column: "store_id",
                filter: "status = 'open'");

            migrationBuilder.CreateIndex(
                name: "ix_staff_store",
                table: "staff",
                columns: new[] { "store_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_count_items_count_id_variant_id_batch_id",
                table: "stock_count_items",
                columns: new[] { "count_id", "variant_id", "batch_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_count_items_count",
                table: "stock_count_items",
                column: "count_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_counts_store",
                table: "stock_counts",
                columns: new[] { "store_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_movements_batch",
                table: "stock_movements",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_movements_reference",
                table: "stock_movements",
                columns: new[] { "reference_type", "reference_id" });

            migrationBuilder.CreateIndex(
                name: "ix_movements_store_date",
                table: "stock_movements",
                columns: new[] { "store_id", "movement_date" });

            migrationBuilder.CreateIndex(
                name: "ix_movements_variant_date",
                table: "stock_movements",
                columns: new[] { "variant_id", "movement_date" });

            migrationBuilder.CreateIndex(
                name: "IX_stores_store_code",
                table: "stores",
                column: "store_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_supplier_variant_variant",
                table: "supplier_variant",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_supplier_code",
                table: "suppliers",
                column: "supplier_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_terminals_store",
                table: "terminals",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ix_txn_items_batch",
                table: "transaction_items",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_txn_items_transaction",
                table: "transaction_items",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_txn_items_variant",
                table: "transaction_items",
                columns: new[] { "variant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_transaction_payments_transaction_id_sequence",
                table: "transaction_payments",
                columns: new[] { "transaction_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_method",
                table: "transaction_payments",
                columns: new[] { "payment_method", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_transaction",
                table: "transaction_payments",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_customer",
                table: "transactions",
                column: "customer_id",
                filter: "customer_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_original",
                table: "transactions",
                column: "original_transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_session",
                table: "transactions",
                column: "cash_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_staff",
                table: "transactions",
                columns: new[] { "staff_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_store_date",
                table: "transactions",
                columns: new[] { "store_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ux_transactions_invoice",
                table: "transactions",
                columns: new[] { "store_id", "invoice_number" },
                unique: true,
                filter: "invoice_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_vav_attribute",
                table: "variant_attribute_values",
                column: "attribute_code");

            migrationBuilder.CreateIndex(
                name: "IX_variants_barcode",
                table: "variants",
                column: "barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_variants_plu",
                table: "variants",
                column: "plu",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_variants_sku",
                table: "variants",
                column: "sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_variants_product",
                table: "variants",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_variants_status",
                table: "variants",
                column: "status");

            migrationBuilder.AddForeignKey(
                name: "FK_batch_items_batches_batch_id",
                table: "batch_items",
                column: "batch_id",
                principalTable: "batches",
                principalColumn: "batch_id");

            migrationBuilder.AddForeignKey(
                name: "FK_batches_purchase_orders_order_id",
                table: "batches",
                column: "order_id",
                principalTable: "purchase_orders",
                principalColumn: "order_id");

            migrationBuilder.AddForeignKey(
                name: "FK_batches_staff_received_by",
                table: "batches",
                column: "received_by",
                principalTable: "staff",
                principalColumn: "staff_id");

            migrationBuilder.AddForeignKey(
                name: "FK_batches_stores_store_id",
                table: "batches",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "store_id");

            migrationBuilder.AddForeignKey(
                name: "FK_cash_movements_cash_sessions_session_id",
                table: "cash_movements",
                column: "session_id",
                principalTable: "cash_sessions",
                principalColumn: "session_id");

            migrationBuilder.AddForeignKey(
                name: "FK_cash_movements_staff_authorised_by",
                table: "cash_movements",
                column: "authorised_by",
                principalTable: "staff",
                principalColumn: "staff_id");

            migrationBuilder.AddForeignKey(
                name: "FK_cash_movements_staff_staff_id",
                table: "cash_movements",
                column: "staff_id",
                principalTable: "staff",
                principalColumn: "staff_id");

            migrationBuilder.AddForeignKey(
                name: "FK_cash_sessions_staff_closed_by",
                table: "cash_sessions",
                column: "closed_by",
                principalTable: "staff",
                principalColumn: "staff_id");

            migrationBuilder.AddForeignKey(
                name: "FK_cash_sessions_staff_opened_by",
                table: "cash_sessions",
                column: "opened_by",
                principalTable: "staff",
                principalColumn: "staff_id");

            migrationBuilder.AddForeignKey(
                name: "FK_cash_sessions_stores_store_id",
                table: "cash_sessions",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "store_id");

            migrationBuilder.AddForeignKey(
                name: "FK_cash_sessions_terminals_terminal_id",
                table: "cash_sessions",
                column: "terminal_id",
                principalTable: "terminals",
                principalColumn: "terminal_id");

            migrationBuilder.AddForeignKey(
                name: "FK_consent_events_staff_captured_by",
                table: "consent_events",
                column: "captured_by",
                principalTable: "staff",
                principalColumn: "staff_id");

            migrationBuilder.AddForeignKey(
                name: "FK_consent_events_terminals_terminal_id",
                table: "consent_events",
                column: "terminal_id",
                principalTable: "terminals",
                principalColumn: "terminal_id");

            migrationBuilder.AddForeignKey(
                name: "FK_credit_movements_returns_return_id",
                table: "credit_movements",
                column: "return_id",
                principalTable: "returns",
                principalColumn: "return_id");

            migrationBuilder.AddForeignKey(
                name: "FK_credit_movements_staff_staff_id",
                table: "credit_movements",
                column: "staff_id",
                principalTable: "staff",
                principalColumn: "staff_id");

            migrationBuilder.AddForeignKey(
                name: "FK_credit_movements_terminals_terminal_id",
                table: "credit_movements",
                column: "terminal_id",
                principalTable: "terminals",
                principalColumn: "terminal_id");

            migrationBuilder.AddForeignKey(
                name: "FK_credit_movements_transactions_transaction_id",
                table: "credit_movements",
                column: "transaction_id",
                principalTable: "transactions",
                principalColumn: "transaction_id");

            migrationBuilder.AddForeignKey(
                name: "FK_data_subject_requests_staff_received_by",
                table: "data_subject_requests",
                column: "received_by",
                principalTable: "staff",
                principalColumn: "staff_id");

            migrationBuilder.AddForeignKey(
                name: "FK_erasure_ledger_staff_executed_by",
                table: "erasure_ledger",
                column: "executed_by",
                principalTable: "staff",
                principalColumn: "staff_id");

            migrationBuilder.AddForeignKey(
                name: "FK_intents_recommendations_fresh_request_id",
                table: "intents",
                column: "fresh_request_id",
                principalTable: "recommendations",
                principalColumn: "recommendation_id");

            migrationBuilder.AddForeignKey(
                name: "FK_intents_stores_store_id",
                table: "intents",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "store_id");

            migrationBuilder.AddForeignKey(
                name: "FK_inventories_stores_store_id",
                table: "inventories",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "store_id");

            migrationBuilder.AddForeignKey(
                name: "FK_loyalty_movements_staff_staff_id",
                table: "loyalty_movements",
                column: "staff_id",
                principalTable: "staff",
                principalColumn: "staff_id");

            migrationBuilder.AddForeignKey(
                name: "FK_loyalty_movements_terminals_terminal_id",
                table: "loyalty_movements",
                column: "terminal_id",
                principalTable: "terminals",
                principalColumn: "terminal_id");

            migrationBuilder.AddForeignKey(
                name: "FK_loyalty_movements_transactions_transaction_id",
                table: "loyalty_movements",
                column: "transaction_id",
                principalTable: "transactions",
                principalColumn: "transaction_id");

            migrationBuilder.AddForeignKey(
                name: "FK_prices_staff_created_by",
                table: "prices",
                column: "created_by",
                principalTable: "staff",
                principalColumn: "staff_id");

            migrationBuilder.AddForeignKey(
                name: "FK_prices_stores_store_id",
                table: "prices",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "store_id");

            migrationBuilder.AddForeignKey(
                name: "FK_processing_log_stores_store_id",
                table: "processing_log",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "store_id");

            migrationBuilder.AddForeignKey(
                name: "FK_processing_log_terminals_terminal_id",
                table: "processing_log",
                column: "terminal_id",
                principalTable: "terminals",
                principalColumn: "terminal_id");

            migrationBuilder.AddForeignKey(
                name: "FK_promotion_product_promotions_promotion_id",
                table: "promotion_product",
                column: "promotion_id",
                principalTable: "promotions",
                principalColumn: "promotion_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_promotion_variant_promotions_promotion_id",
                table: "promotion_variant",
                column: "promotion_id",
                principalTable: "promotions",
                principalColumn: "promotion_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_promotions_recommendations_source_recommendation_id",
                table: "promotions",
                column: "source_recommendation_id",
                principalTable: "recommendations",
                principalColumn: "recommendation_id");

            migrationBuilder.AddForeignKey(
                name: "FK_promotions_staff_created_by",
                table: "promotions",
                column: "created_by",
                principalTable: "staff",
                principalColumn: "staff_id");

            migrationBuilder.AddForeignKey(
                name: "FK_promotions_stores_store_id",
                table: "promotions",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "store_id");

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_order_items_purchase_orders_order_id",
                table: "purchase_order_items",
                column: "order_id",
                principalTable: "purchase_orders",
                principalColumn: "order_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_orders_recommendations_source_recommendation_id",
                table: "purchase_orders",
                column: "source_recommendation_id",
                principalTable: "recommendations",
                principalColumn: "recommendation_id");

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_orders_staff_created_by",
                table: "purchase_orders",
                column: "created_by",
                principalTable: "staff",
                principalColumn: "staff_id");

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_orders_stores_store_id",
                table: "purchase_orders",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "store_id");

            migrationBuilder.AddForeignKey(
                name: "FK_recommendation_decisions_recommendation_options_chosen_option_id",
                table: "recommendation_decisions",
                column: "chosen_option_id",
                principalTable: "recommendation_options",
                principalColumn: "option_id");

            migrationBuilder.AddForeignKey(
                name: "FK_recommendation_decisions_recommendations_recommendation_id",
                table: "recommendation_decisions",
                column: "recommendation_id",
                principalTable: "recommendations",
                principalColumn: "recommendation_id");

            migrationBuilder.AddForeignKey(
                name: "FK_recommendation_decisions_staff_decided_by",
                table: "recommendation_decisions",
                column: "decided_by",
                principalTable: "staff",
                principalColumn: "staff_id");

            migrationBuilder.AddForeignKey(
                name: "FK_recommendation_decisions_terminals_terminal_id",
                table: "recommendation_decisions",
                column: "terminal_id",
                principalTable: "terminals",
                principalColumn: "terminal_id");

            migrationBuilder.AddForeignKey(
                name: "FK_recommendation_options_recommendations_recommendation_id",
                table: "recommendation_options",
                column: "recommendation_id",
                principalTable: "recommendations",
                principalColumn: "recommendation_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_recommendations_stores_store_id",
                table: "recommendations",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "store_id");

            migrationBuilder.AddForeignKey(
                name: "FK_returns_staff_approved_by",
                table: "returns",
                column: "approved_by",
                principalTable: "staff",
                principalColumn: "staff_id");

            migrationBuilder.AddForeignKey(
                name: "FK_returns_staff_staff_id",
                table: "returns",
                column: "staff_id",
                principalTable: "staff",
                principalColumn: "staff_id");

            migrationBuilder.AddForeignKey(
                name: "FK_returns_stores_store_id",
                table: "returns",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "store_id");

            migrationBuilder.AddForeignKey(
                name: "FK_returns_terminals_terminal_id",
                table: "returns",
                column: "terminal_id",
                principalTable: "terminals",
                principalColumn: "terminal_id");

            migrationBuilder.AddForeignKey(
                name: "FK_returns_transaction_items_transaction_item_id",
                table: "returns",
                column: "transaction_item_id",
                principalTable: "transaction_items",
                principalColumn: "transaction_item_id");

            migrationBuilder.AddForeignKey(
                name: "FK_shifts_staff_staff_id",
                table: "shifts",
                column: "staff_id",
                principalTable: "staff",
                principalColumn: "staff_id");

            migrationBuilder.AddForeignKey(
                name: "FK_shifts_stores_store_id",
                table: "shifts",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "store_id");

            migrationBuilder.AddForeignKey(
                name: "FK_shifts_terminals_terminal_id",
                table: "shifts",
                column: "terminal_id",
                principalTable: "terminals",
                principalColumn: "terminal_id");

            migrationBuilder.AddForeignKey(
                name: "FK_staff_stores_store_id",
                table: "staff",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "store_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_stores_staff_manager_id",
                table: "stores");

            migrationBuilder.DropTable(
                name: "batch_items");

            migrationBuilder.DropTable(
                name: "cash_movements");

            migrationBuilder.DropTable(
                name: "category_attributes");

            migrationBuilder.DropTable(
                name: "consent_events");

            migrationBuilder.DropTable(
                name: "credit_movements");

            migrationBuilder.DropTable(
                name: "erasure_ledger");

            migrationBuilder.DropTable(
                name: "inbox");

            migrationBuilder.DropTable(
                name: "intents");

            migrationBuilder.DropTable(
                name: "inventories");

            migrationBuilder.DropTable(
                name: "loyalty_movements");

            migrationBuilder.DropTable(
                name: "outbox");

            migrationBuilder.DropTable(
                name: "parameter_registry");

            migrationBuilder.DropTable(
                name: "prices");

            migrationBuilder.DropTable(
                name: "processing_log");

            migrationBuilder.DropTable(
                name: "product_attribute_values");

            migrationBuilder.DropTable(
                name: "product_bundle_items");

            migrationBuilder.DropTable(
                name: "product_category");

            migrationBuilder.DropTable(
                name: "promotion_product");

            migrationBuilder.DropTable(
                name: "promotion_variant");

            migrationBuilder.DropTable(
                name: "purchase_order_items");

            migrationBuilder.DropTable(
                name: "recommendation_decisions");

            migrationBuilder.DropTable(
                name: "retention_policies");

            migrationBuilder.DropTable(
                name: "schema_migrations");

            migrationBuilder.DropTable(
                name: "shifts");

            migrationBuilder.DropTable(
                name: "stock_count_items");

            migrationBuilder.DropTable(
                name: "stock_movements");

            migrationBuilder.DropTable(
                name: "store_entitlements");

            migrationBuilder.DropTable(
                name: "supplier_variant");

            migrationBuilder.DropTable(
                name: "sync_state");

            migrationBuilder.DropTable(
                name: "system_config");

            migrationBuilder.DropTable(
                name: "transaction_payments");

            migrationBuilder.DropTable(
                name: "variant_attribute_values");

            migrationBuilder.DropTable(
                name: "returns");

            migrationBuilder.DropTable(
                name: "data_subject_requests");

            migrationBuilder.DropTable(
                name: "product_bundles");

            migrationBuilder.DropTable(
                name: "recommendation_options");

            migrationBuilder.DropTable(
                name: "stock_counts");

            migrationBuilder.DropTable(
                name: "attribute_options");

            migrationBuilder.DropTable(
                name: "transaction_items");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "attribute_definitions");

            migrationBuilder.DropTable(
                name: "batches");

            migrationBuilder.DropTable(
                name: "promotions");

            migrationBuilder.DropTable(
                name: "transactions");

            migrationBuilder.DropTable(
                name: "variants");

            migrationBuilder.DropTable(
                name: "purchase_orders");

            migrationBuilder.DropTable(
                name: "cash_sessions");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "reason_codes");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "units_of_measure");

            migrationBuilder.DropTable(
                name: "recommendations");

            migrationBuilder.DropTable(
                name: "suppliers");

            migrationBuilder.DropTable(
                name: "terminals");

            migrationBuilder.DropTable(
                name: "staff");

            migrationBuilder.DropTable(
                name: "notice_versions");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "stores");
        }
    }
}
