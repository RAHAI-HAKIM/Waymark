using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Waymark.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConsentGivenBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "given_by",
                table: "consent_events",
                type: "TEXT",
                nullable: false,
                defaultValue: "subject");

            migrationBuilder.AddCheckConstraint(
                name: "ck_consent_events_given_by",
                table: "consent_events",
                sql: "given_by IN ('subject','legal_representative')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_consent_events_given_by",
                table: "consent_events");

            migrationBuilder.DropColumn(
                name: "given_by",
                table: "consent_events");
        }
    }
}
