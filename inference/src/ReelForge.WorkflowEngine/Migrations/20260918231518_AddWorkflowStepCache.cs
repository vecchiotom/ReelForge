using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReelForge.WorkflowEngine.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowStepCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cache_mode",
                table: "workflow_steps",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "cached_tokens_saved",
                table: "workflow_step_results",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "from_cache",
                table: "workflow_step_results",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "workflow_step_cache_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cache_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    workflow_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_type = table.Column<string>(type: "text", nullable: false),
                    agent_type = table.Column<string>(type: "text", nullable: false),
                    output = table.Column<string>(type: "text", nullable: false),
                    output_storage_key = table.Column<string>(type: "text", nullable: true),
                    artifact_storage_key = table.Column<string>(type: "text", nullable: true),
                    chat_transcript_json = table.Column<string>(type: "jsonb", nullable: true),
                    tokens_used = table.Column<int>(type: "integer", nullable: false),
                    input_tokens = table.Column<int>(type: "integer", nullable: true),
                    output_tokens = table.Column<int>(type: "integer", nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_hit_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    hit_count = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_step_cache_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_step_cache_entries_project_id_cache_key",
                table: "workflow_step_cache_entries",
                columns: new[] { "project_id", "cache_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_step_cache_entries_project_id_expires_at",
                table: "workflow_step_cache_entries",
                columns: new[] { "project_id", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflow_step_cache_entries");

            migrationBuilder.DropColumn(
                name: "cache_mode",
                table: "workflow_steps");

            migrationBuilder.DropColumn(
                name: "cached_tokens_saved",
                table: "workflow_step_results");

            migrationBuilder.DropColumn(
                name: "from_cache",
                table: "workflow_step_results");
        }
    }
}
