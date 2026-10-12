using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Waymark.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BlindCountAndClosedSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_cash_sessions_one_open",
                table: "cash_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cash_sessions_status_2",
                table: "cash_sessions");

            migrationBuilder.AddColumn<long>(
                name: "pending_count",
                table: "cash_sessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "pending_count_moves",
                table: "cash_sessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_cash_sessions_one_open",
                table: "cash_sessions",
                column: "terminal_id",
                unique: true,
                filter: "status <> 'closed'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cash_sessions_pending_count",
                table: "cash_sessions",
                sql: "(pending_count IS NULL AND pending_count_moves IS NULL) OR (pending_count >= 0 AND pending_count_moves >= 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cash_sessions_status_2",
                table: "cash_sessions",
                sql: "status <> 'closed' OR (counted_cash IS NOT NULL AND expected_cash IS NOT NULL AND variance IS NOT NULL AND z_report_number IS NOT NULL AND closed_at IS NOT NULL AND closed_by IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_cash_sessions_one_open",
                table: "cash_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cash_sessions_pending_count",
                table: "cash_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cash_sessions_status_2",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "pending_count",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "pending_count_moves",
                table: "cash_sessions");

            migrationBuilder.CreateIndex(
                name: "ux_cash_sessions_one_open",
                table: "cash_sessions",
                column: "terminal_id",
                unique: true,
                filter: "status = 'open'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cash_sessions_status_2",
                table: "cash_sessions",
                sql: "status <> 'closed' OR counted_cash IS NOT NULL");
        }
    }
}
