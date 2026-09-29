using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Waymark.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OverridesAndDiscountReasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "discount_authorised_by",
                table: "transactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "discount_note",
                table: "transactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "discount_reason_code",
                table: "transactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "discount_note",
                table: "transaction_items",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "list_price",
                table: "transaction_items",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "override_authorised_by",
                table: "transaction_items",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "override_reason_code",
                table: "transaction_items",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_discount_reason",
                table: "transactions",
                sql: "discount_reason_code IS NULL OR discount_authorised_by IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transaction_items_list_price",
                table: "transaction_items",
                sql: "list_price IS NULL OR list_price >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transaction_items_override",
                table: "transaction_items",
                sql: "(list_price IS NULL AND override_reason_code IS NULL AND override_authorised_by IS NULL) OR (list_price IS NOT NULL AND override_reason_code IS NOT NULL AND override_authorised_by IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_transaction_items_reason_codes_override_reason_code",
                table: "transaction_items",
                column: "override_reason_code",
                principalTable: "reason_codes",
                principalColumn: "reason_code");

            migrationBuilder.AddForeignKey(
                name: "FK_transaction_items_staff_override_authorised_by",
                table: "transaction_items",
                column: "override_authorised_by",
                principalTable: "staff",
                principalColumn: "staff_id");

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_reason_codes_discount_reason_code",
                table: "transactions",
                column: "discount_reason_code",
                principalTable: "reason_codes",
                principalColumn: "reason_code");

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_staff_discount_authorised_by",
                table: "transactions",
                column: "discount_authorised_by",
                principalTable: "staff",
                principalColumn: "staff_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transaction_items_reason_codes_override_reason_code",
                table: "transaction_items");

            migrationBuilder.DropForeignKey(
                name: "FK_transaction_items_staff_override_authorised_by",
                table: "transaction_items");

            migrationBuilder.DropForeignKey(
                name: "FK_transactions_reason_codes_discount_reason_code",
                table: "transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_transactions_staff_discount_authorised_by",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_discount_reason",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transaction_items_list_price",
                table: "transaction_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transaction_items_override",
                table: "transaction_items");

            migrationBuilder.DropColumn(
                name: "discount_authorised_by",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "discount_note",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "discount_reason_code",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "discount_note",
                table: "transaction_items");

            migrationBuilder.DropColumn(
                name: "list_price",
                table: "transaction_items");

            migrationBuilder.DropColumn(
                name: "override_authorised_by",
                table: "transaction_items");

            migrationBuilder.DropColumn(
                name: "override_reason_code",
                table: "transaction_items");
        }
    }
}
