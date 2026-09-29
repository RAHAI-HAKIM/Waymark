using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Waymark.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WeighedGoods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "quantity_source",
                table: "transaction_items",
                type: "TEXT",
                nullable: false,
                defaultValue: "count");

            migrationBuilder.AddColumn<string>(
                name: "scale_label_format",
                table: "stores",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_transaction_items_quantity_source",
                table: "transaction_items",
                sql: "quantity_source IN ('count','typed_weight','label_weight','label_price')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_transaction_items_quantity_source",
                table: "transaction_items");

            migrationBuilder.DropColumn(
                name: "quantity_source",
                table: "transaction_items");

            migrationBuilder.DropColumn(
                name: "scale_label_format",
                table: "stores");
        }
    }
}
