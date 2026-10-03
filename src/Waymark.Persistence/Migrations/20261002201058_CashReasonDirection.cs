using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Waymark.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashReasonDirection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "direction",
                table: "reason_codes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_reason_codes_direction",
                table: "reason_codes",
                sql: "direction IS NULL OR (direction IN ('in','out') AND applies_to = 'cash_movement')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_reason_codes_direction",
                table: "reason_codes");

            migrationBuilder.DropColumn(
                name: "direction",
                table: "reason_codes");
        }
    }
}
