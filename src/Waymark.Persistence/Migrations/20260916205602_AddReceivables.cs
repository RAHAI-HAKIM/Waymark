using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Waymark.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReceivables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_returns_refund_method",
                table: "returns");

            migrationBuilder.AddColumn<string>(
                name: "refund_transaction_item_id",
                table: "returns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "credit_limit",
                table: "customers",
                type: "INTEGER",
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
                    occurred_at = table.Column<string>(type: "TEXT", nullable: false),
                    payment_id = table.Column<string>(type: "TEXT", nullable: true),
                    cash_movement_id = table.Column<string>(type: "TEXT", nullable: true),
                    reason_code = table.Column<string>(type: "TEXT", nullable: true),
                    staff_id = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receivable_movements", x => x.movement_id);
                    table.CheckConstraint("ck_receivable_movements_amount", "amount <> 0");
                    table.CheckConstraint("ck_receivable_movements_cash_movement_id", "cash_movement_id IS NULL OR movement_type = 'payment'");
                    table.CheckConstraint("ck_receivable_movements_movement_type", "movement_type IN ('charge','payment','adjustment','write_off')");
                    table.CheckConstraint("ck_receivable_movements_payment_id", "(movement_type = 'charge') = (payment_id IS NOT NULL)");
                    table.CheckConstraint("ck_receivable_movements_reason_code", "movement_type NOT IN ('adjustment','write_off') OR reason_code IS NOT NULL");
                    table.CheckConstraint("ck_receivable_movements_sign", "movement_type IN ('charge','adjustment') OR amount < 0");
                    table.ForeignKey(
                        name: "FK_receivable_movements_cash_movements_cash_movement_id",
                        column: x => x.cash_movement_id,
                        principalTable: "cash_movements",
                        principalColumn: "movement_id");
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
                        name: "FK_receivable_movements_staff_staff_id",
                        column: x => x.staff_id,
                        principalTable: "staff",
                        principalColumn: "staff_id");
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

            migrationBuilder.AddCheckConstraint(
                name: "ck_returns_refund_method",
                table: "returns",
                sql: "refund_method IN ('cash','card','store_credit','exchange','on_account')");

            migrationBuilder.CreateIndex(
                name: "ix_receivable_customer",
                table: "receivable_movements",
                columns: new[] { "customer_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ux_receivable_cash_movement",
                table: "receivable_movements",
                column: "cash_movement_id",
                unique: true,
                filter: "cash_movement_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_receivable_payment",
                table: "receivable_movements",
                column: "payment_id",
                unique: true,
                filter: "payment_id IS NOT NULL");

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

            migrationBuilder.DropCheckConstraint(
                name: "ck_returns_refund_method",
                table: "returns");

            migrationBuilder.DropColumn(
                name: "refund_transaction_item_id",
                table: "returns");

            migrationBuilder.DropColumn(
                name: "credit_limit",
                table: "customers");

            migrationBuilder.AddCheckConstraint(
                name: "ck_returns_refund_method",
                table: "returns",
                sql: "refund_method IN ('cash','card','store_credit','exchange')");
        }
    }
}
