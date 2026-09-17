using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Waymark.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameRoundingPolicyChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_store_rounding_policy",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_store_rounding_policy",
                table: "stores");

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
                name: "ck_transactions_rounding_policy",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stores_rounding_policy",
                table: "stores");

            migrationBuilder.AddCheckConstraint(
                name: "ck_store_rounding_policy",
                table: "transactions",
                sql: "rounding_policy IN ('half_even','half_up')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_store_rounding_policy",
                table: "stores",
                sql: "rounding_policy IN ('half_even','half_up')");
        }
    }
}
