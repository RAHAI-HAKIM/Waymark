using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Waymark.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashSessionClose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "closed_authorised_by",
                table: "cash_sessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_cash_sessions_one_open",
                table: "cash_sessions",
                column: "terminal_id",
                unique: true,
                filter: "status = 'open'");

            migrationBuilder.CreateIndex(
                name: "ux_cash_sessions_z_number",
                table: "cash_sessions",
                columns: new[] { "terminal_id", "z_report_number" },
                unique: true,
                filter: "z_report_number IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cash_sessions_closed_authorised_by",
                table: "cash_sessions",
                sql: "closed_authorised_by IS NULL OR status = 'closed'");

            migrationBuilder.AddForeignKey(
                name: "FK_cash_sessions_staff_closed_authorised_by",
                table: "cash_sessions",
                column: "closed_authorised_by",
                principalTable: "staff",
                principalColumn: "staff_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_cash_sessions_staff_closed_authorised_by",
                table: "cash_sessions");

            migrationBuilder.DropIndex(
                name: "ux_cash_sessions_one_open",
                table: "cash_sessions");

            migrationBuilder.DropIndex(
                name: "ux_cash_sessions_z_number",
                table: "cash_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cash_sessions_closed_authorised_by",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "closed_authorised_by",
                table: "cash_sessions");
        }
    }
}
