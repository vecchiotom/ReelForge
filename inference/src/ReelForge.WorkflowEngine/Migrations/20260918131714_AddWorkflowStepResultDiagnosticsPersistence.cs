using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReelForge.WorkflowEngine.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowStepResultDiagnosticsPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "chat_transcript_json",
                table: "workflow_step_results",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reasoning_json",
                table: "workflow_step_results",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tool_calls_json",
                table: "workflow_step_results",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "chat_transcript_json",
                table: "workflow_step_results");

            migrationBuilder.DropColumn(
                name: "reasoning_json",
                table: "workflow_step_results");

            migrationBuilder.DropColumn(
                name: "tool_calls_json",
                table: "workflow_step_results");
        }
    }
}
