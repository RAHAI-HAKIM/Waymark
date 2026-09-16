using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Waymark.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReceivableMovement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "refund_transaction_item_id",
                table: "returns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "receivable_movements",
                columns: table => new
                {
                    movement_id = table.Column<string>(type: "TEXT", nullable: false),
                    store_id = table.Column<string>(type: "TEXT", nullable: false),
                    customer_id = table.Column<string>(type: "TEXT", nullable: false),
                    movement_type = table.Column<string>(type: "TEXT", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    occured_at = table.Column<string>(type: "TEXT", nullable: false),
                    payment_id = table.Column<string>(type: "TEXT", nullable: true),
                    reason_code = table.Column<string>(type: "TEXT", nullable: true),
                    staff_id = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receivable_movements", x => x.movement_id);
                    table.CheckConstraint("ck_credit_movements_amount", "amount <> 0");
                    table.CheckConstraint("ck_receivable_movements_movement_type", "movement_type IN ('charge', 'payment', 'adjustment', 'write_off')");
                    table.ForeignKey(
                        name: "FK_receivable_movements_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "customer_id");
                    table.ForeignKey(
                        name: "FK_receivable_movements_reason_codes_reason_code",
                        column: x => x.reason_code,
                        principalTable: "reason_codes",
                        principalColumn: "reason_code");
                    table.ForeignKey(
                        name: "FK_receivable_movements_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "store_id");
                    table.ForeignKey(
                        name: "FK_receivable_movements_transaction_payments_payment_id",
                        column: x => x.payment_id,
                        principalTable: "transaction_payments",
                        principalColumn: "payment_id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_receivable_movements_customer_id_occured_at",
                table: "receivable_movements",
                columns: new[] { "customer_id", "occured_at" });

            migrationBuilder.AddForeignKey(
                name: "FK_returns_transaction_items_refund_transaction_item_id",
                table: "returns",
                column: "refund_transaction_item_id",
                principalTable: "transaction_items",
                principalColumn: "transaction_item_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_returns_transaction_items_refund_transaction_item_id",
                table: "returns");

            migrationBuilder.DropTable(
                name: "receivable_movements");

            migrationBuilder.DropColumn(
                name: "refund_transaction_item_id",
                table: "returns");
        }
    }
}
