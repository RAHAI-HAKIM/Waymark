using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Waymark.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoundingVariance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rounding_variance",
                columns: table => new
                {
                    variance_id = table.Column<string>(type: "TEXT", nullable: false),
                    store_id = table.Column<string>(type: "TEXT", nullable: false),
                    occurred_at = table.Column<string>(type: "TEXT", nullable: false),
                    reference_type = table.Column<string>(type: "TEXT", nullable: false),
                    reference_id = table.Column<string>(type: "TEXT", nullable: false),
                    source = table.Column<string>(type: "TEXT", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    policy = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rounding_variance", x => x.variance_id);
                    table.CheckConstraint("ck_rounding_variance_amount", "amount <> 0");
                    table.CheckConstraint("ck_rounding_variance_policy", "policy IN ('half_even','half_up')");
                    table.CheckConstraint("ck_rounding_variance_reference_type", "reference_type IN ('transaction','purchase_order','batch')");
                    table.CheckConstraint("ck_rounding_variance_source", "source IN ('cash_tender','currency_conversion')");
                    table.ForeignKey(
                        name: "FK_rounding_variance_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "store_id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_rounding_variance_reference",
                table: "rounding_variance",
                columns: new[] { "reference_type", "reference_id" });

            migrationBuilder.CreateIndex(
                name: "ix_rounding_variance_store_date",
                table: "rounding_variance",
                columns: new[] { "store_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rounding_variance");
        }
    }
}
