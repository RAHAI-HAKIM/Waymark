using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Waymark.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LogbookOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_retention_policies_entity_type",
                table: "retention_policies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_log_actor_type",
                table: "processing_log");

            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_log_operation",
                table: "processing_log");

            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_log_subject_type",
                table: "processing_log");

            migrationBuilder.DropCheckConstraint(
                name: "ck_erasure_ledger_subject_type",
                table: "erasure_ledger");

            migrationBuilder.AddColumn<long>(
                name: "subject_count",
                table: "processing_log",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_retention_policies_entity_type",
                table: "retention_policies",
                sql: "entity_type IN ('transaction','customer','staff','processing_log', 'consent_event','recommendation','stock_movement', 'receivable','supplier_contact','data_subject_request','tier2_record')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_log_actor_type",
                table: "processing_log",
                sql: "actor_type IN ('staff','system','engine','processor')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_log_operation",
                table: "processing_log",
                sql: "operation IN ('collection','consultation','disclosure','transmission', 'modification','erasure','pseudonymisation','re_identification', 'recording','organisation','storage','extraction','use','dissemination','alignment','interconnection','locking','encryption','destruction')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_log_subject_count",
                table: "processing_log",
                sql: "subject_count IS NULL OR (subject_count > 0 AND subject_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_log_subject_type",
                table: "processing_log",
                sql: "subject_type IN ('customer','staff','supplier')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_erasure_ledger_subject_type",
                table: "erasure_ledger",
                sql: "subject_type IN ('customer','staff','supplier')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_retention_policies_entity_type",
                table: "retention_policies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_log_actor_type",
                table: "processing_log");

            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_log_operation",
                table: "processing_log");

            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_log_subject_count",
                table: "processing_log");

            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_log_subject_type",
                table: "processing_log");

            migrationBuilder.DropCheckConstraint(
                name: "ck_erasure_ledger_subject_type",
                table: "erasure_ledger");

            migrationBuilder.DropColumn(
                name: "subject_count",
                table: "processing_log");

            migrationBuilder.AddCheckConstraint(
                name: "ck_retention_policies_entity_type",
                table: "retention_policies",
                sql: "entity_type IN ('transaction','customer','staff','processing_log', 'consent_event','recommendation','stock_movement')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_log_actor_type",
                table: "processing_log",
                sql: "actor_type IN ('staff','system','engine')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_log_operation",
                table: "processing_log",
                sql: "operation IN ('collection','consultation','disclosure','transmission', 'modification','erasure','pseudonymisation','re_identification')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_log_subject_type",
                table: "processing_log",
                sql: "subject_type IN ('customer','staff')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_erasure_ledger_subject_type",
                table: "erasure_ledger",
                sql: "subject_type IN ('customer','staff')");
        }
    }
}
