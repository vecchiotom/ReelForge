using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReelForge.WorkflowEngine.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoEditingStepConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "video_analyze_config_json",
                table: "workflow_steps",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "video_compile_config_json",
                table: "workflow_steps",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "artifact_storage_key",
                table: "workflow_step_results",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "video_analyze_config_json",
                table: "workflow_steps");

            migrationBuilder.DropColumn(
                name: "video_compile_config_json",
                table: "workflow_steps");

            migrationBuilder.DropColumn(
                name: "artifact_storage_key",
                table: "workflow_step_results");
        }
    }
}
