using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Waymark.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalesVoid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "payment_opened_at",
                table: "transactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "void_authorised_by",
                table: "transactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "removed_at",
                table: "transaction_items",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "removed_by",
                table: "transaction_items",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_void_facts",
                table: "transactions",
                sql: "status = 'voided' OR (payment_opened_at IS NULL AND void_authorised_by IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transaction_items_removed",
                table: "transaction_items",
                sql: "(removed_at IS NULL) = (removed_by IS NULL) AND (removed_at IS NULL OR batch_id IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_transaction_items_staff_removed_by",
                table: "transaction_items",
                column: "removed_by",
                principalTable: "staff",
                principalColumn: "staff_id");

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_staff_void_authorised_by",
                table: "transactions",
                column: "void_authorised_by",
                principalTable: "staff",
                principalColumn: "staff_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transaction_items_staff_removed_by",
                table: "transaction_items");

            migrationBuilder.DropForeignKey(
                name: "FK_transactions_staff_void_authorised_by",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_void_facts",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transaction_items_removed",
                table: "transaction_items");

            migrationBuilder.DropColumn(
                name: "payment_opened_at",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "void_authorised_by",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "removed_at",
                table: "transaction_items");

            migrationBuilder.DropColumn(
                name: "removed_by",
                table: "transaction_items");
        }
    }
}
