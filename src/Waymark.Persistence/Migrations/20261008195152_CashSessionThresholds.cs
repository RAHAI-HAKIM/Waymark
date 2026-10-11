using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Waymark.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashSessionThresholds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "variance_alert_value",
                table: "cash_sessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "void_alert_count",
                table: "cash_sessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "void_alert_value",
                table: "cash_sessions",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "variance_alert_value",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "void_alert_count",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "void_alert_value",
                table: "cash_sessions");
        }
    }
}
