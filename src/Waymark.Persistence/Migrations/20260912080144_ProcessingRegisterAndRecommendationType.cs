using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Waymark.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProcessingRegisterAndRecommendationType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "recommendation_type",
                table: "recommendations",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "legal_basis",
                table: "processing_log",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "processing_counters",
                columns: table => new
                {
                    counter_id = table.Column<string>(type: "TEXT", nullable: false),
                    store_id = table.Column<string>(type: "TEXT", nullable: true),
                    day = table.Column<string>(type: "TEXT", nullable: false),
                    operation = table.Column<string>(type: "TEXT", nullable: false),
                    purpose = table.Column<string>(type: "TEXT", nullable: false),
                    event_count = table.Column<long>(type: "INTEGER", nullable: false),
                    rolled_up_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processing_counters", x => x.counter_id);
                    table.CheckConstraint("ck_processing_counters_event_count", "event_count > 0");
                    table.ForeignKey(
                        name: "FK_processing_counters_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "store_id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_recs_dedupe",
                table: "recommendations",
                columns: new[] { "store_id", "recommendation_type", "subject_type", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ux_processing_counters_day",
                table: "processing_counters",
                columns: new[] { "store_id", "day", "operation", "purpose" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processing_counters");

            migrationBuilder.DropIndex(
                name: "ix_recs_dedupe",
                table: "recommendations");

            migrationBuilder.DropColumn(
                name: "recommendation_type",
                table: "recommendations");

            migrationBuilder.DropColumn(
                name: "legal_basis",
                table: "processing_log");
        }
    }
}
