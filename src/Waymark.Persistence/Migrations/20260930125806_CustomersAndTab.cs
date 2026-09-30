using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Waymark.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomersAndTab : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_notice_versions_notice_type",
                table: "notice_versions");

            migrationBuilder.AddColumn<string>(
                name: "override_authorised_by",
                table: "receivable_movements",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "collection_notice_version",
                table: "customers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tab_frozen_at",
                table: "customers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "credit_limit_events",
                columns: table => new
                {
                    event_id = table.Column<string>(type: "TEXT", nullable: false),
                    customer_id = table.Column<string>(type: "TEXT", nullable: false),
                    event_type = table.Column<string>(type: "TEXT", nullable: false),
                    previous_limit = table.Column<long>(type: "INTEGER", nullable: true),
                    new_limit = table.Column<long>(type: "INTEGER", nullable: true),
                    staff_id = table.Column<string>(type: "TEXT", nullable: false),
                    occurred_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_limit_events", x => x.event_id);
                    table.CheckConstraint("ck_credit_limit_events_event_type", "event_type IN ('set','frozen','unfrozen')");
                    table.CheckConstraint("ck_credit_limit_events_limits", "event_type = 'set' OR (previous_limit IS NULL AND new_limit IS NULL)");
                    table.CheckConstraint("ck_credit_limit_events_new_limit", "new_limit IS NULL OR new_limit >= 0");
                    table.ForeignKey(
                        name: "FK_credit_limit_events_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "customer_id");
                    table.ForeignKey(
                        name: "FK_credit_limit_events_staff_staff_id",
                        column: x => x.staff_id,
                        principalTable: "staff",
                        principalColumn: "staff_id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_receivable_movements_override",
                table: "receivable_movements",
                sql: "override_authorised_by IS NULL OR movement_type = 'charge'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notice_versions_notice_type",
                table: "notice_versions",
                sql: "notice_type IN ('processing','marketing','staff','information')");

            migrationBuilder.CreateIndex(
                name: "ix_credit_limit_events_customer",
                table: "credit_limit_events",
                columns: new[] { "customer_id", "occurred_at" });

            migrationBuilder.AddForeignKey(
                name: "FK_customers_notice_versions_collection_notice_version",
                table: "customers",
                column: "collection_notice_version",
                principalTable: "notice_versions",
                principalColumn: "version_code");

            migrationBuilder.AddForeignKey(
                name: "FK_receivable_movements_staff_override_authorised_by",
                table: "receivable_movements",
                column: "override_authorised_by",
                principalTable: "staff",
                principalColumn: "staff_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_customers_notice_versions_collection_notice_version",
                table: "customers");

            migrationBuilder.DropForeignKey(
                name: "FK_receivable_movements_staff_override_authorised_by",
                table: "receivable_movements");

            migrationBuilder.DropTable(
                name: "credit_limit_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_receivable_movements_override",
                table: "receivable_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notice_versions_notice_type",
                table: "notice_versions");

            migrationBuilder.DropColumn(
                name: "override_authorised_by",
                table: "receivable_movements");

            migrationBuilder.DropColumn(
                name: "collection_notice_version",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "tab_frozen_at",
                table: "customers");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notice_versions_notice_type",
                table: "notice_versions",
                sql: "notice_type IN ('processing','marketing','staff')");
        }
    }
}
