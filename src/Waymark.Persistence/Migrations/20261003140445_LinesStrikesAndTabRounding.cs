using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Waymark.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinesStrikesAndTabRounding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_rounding_variance_reference_type",
                table: "rounding_variance");

            migrationBuilder.AddColumn<int>(
                name: "line_number",
                table: "transaction_items",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "removed_authorised_by",
                table: "transaction_items",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_transaction_items_line_number",
                table: "transaction_items",
                sql: "line_number >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transaction_items_removed_authorised",
                table: "transaction_items",
                sql: "removed_authorised_by IS NULL OR removed_at IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rounding_variance_reference_type",
                table: "rounding_variance",
                sql: "reference_type IN ('transaction','purchase_order','batch','receivable_movement')");

            migrationBuilder.AddForeignKey(
                name: "FK_transaction_items_staff_removed_authorised_by",
                table: "transaction_items",
                column: "removed_authorised_by",
                principalTable: "staff",
                principalColumn: "staff_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transaction_items_staff_removed_authorised_by",
                table: "transaction_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transaction_items_line_number",
                table: "transaction_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transaction_items_removed_authorised",
                table: "transaction_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rounding_variance_reference_type",
                table: "rounding_variance");

            migrationBuilder.DropColumn(
                name: "line_number",
                table: "transaction_items");

            migrationBuilder.DropColumn(
                name: "removed_authorised_by",
                table: "transaction_items");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rounding_variance_reference_type",
                table: "rounding_variance",
                sql: "reference_type IN ('transaction','purchase_order','batch')");
        }
    }
}
