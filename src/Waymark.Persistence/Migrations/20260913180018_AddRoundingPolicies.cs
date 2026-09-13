using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Waymark.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoundingPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "rounding_policy",
                table: "transactions",
                type: "TEXT",
                nullable: false,
                defaultValue: "half_up");

            migrationBuilder.AddColumn<string>(
                name: "rounding_policy",
                table: "stores",
                type: "TEXT",
                nullable: false,
                defaultValue: "half_up");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_rounding_policy",
                table: "transactions",
                sql: "rounding_policy IN ('half_even','half_up')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stores_rounding_policy",
                table: "stores",
                sql: "rounding_policy IN ('half_even','half_up')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_store_rounding_policy",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_store_rounding_policy",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "rounding_policy",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "rounding_policy",
                table: "stores");
        }
    }
}
