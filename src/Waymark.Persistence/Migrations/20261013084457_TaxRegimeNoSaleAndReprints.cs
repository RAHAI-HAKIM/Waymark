using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Waymark.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaxRegimeNoSaleAndReprints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "reprint_count",
                table: "transactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "tax_regime",
                table: "stores",
                type: "TEXT",
                nullable: false,
                defaultValue: "not_subject");

            migrationBuilder.CreateTable(
                name: "no_sale_openings",
                columns: table => new
                {
                    no_sale_id = table.Column<string>(type: "TEXT", nullable: false),
                    store_id = table.Column<string>(type: "TEXT", nullable: false),
                    session_id = table.Column<string>(type: "TEXT", nullable: false),
                    terminal_id = table.Column<string>(type: "TEXT", nullable: false),
                    staff_id = table.Column<string>(type: "TEXT", nullable: false),
                    authorised_by = table.Column<string>(type: "TEXT", nullable: true),
                    reason_code = table.Column<string>(type: "TEXT", nullable: false),
                    note = table.Column<string>(type: "TEXT", nullable: true),
                    occurred_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_no_sale_openings", x => x.no_sale_id);
                    table.ForeignKey(
                        name: "FK_no_sale_openings_cash_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "cash_sessions",
                        principalColumn: "session_id");
                    table.ForeignKey(
                        name: "FK_no_sale_openings_reason_codes_reason_code",
                        column: x => x.reason_code,
                        principalTable: "reason_codes",
                        principalColumn: "reason_code");
                    table.ForeignKey(
                        name: "FK_no_sale_openings_staff_authorised_by",
                        column: x => x.authorised_by,
                        principalTable: "staff",
                        principalColumn: "staff_id");
                    table.ForeignKey(
                        name: "FK_no_sale_openings_staff_staff_id",
                        column: x => x.staff_id,
                        principalTable: "staff",
                        principalColumn: "staff_id");
                    table.ForeignKey(
                        name: "FK_no_sale_openings_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "store_id");
                    table.ForeignKey(
                        name: "FK_no_sale_openings_terminals_terminal_id",
                        column: x => x.terminal_id,
                        principalTable: "terminals",
                        principalColumn: "terminal_id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_stores_tax_regime",
                table: "stores",
                sql: "tax_regime IN ('not_subject','subject')");

            migrationBuilder.CreateIndex(
                name: "ix_no_sale_openings_session",
                table: "no_sale_openings",
                column: "session_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "no_sale_openings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stores_tax_regime",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "reprint_count",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "tax_regime",
                table: "stores");
        }
    }
}
