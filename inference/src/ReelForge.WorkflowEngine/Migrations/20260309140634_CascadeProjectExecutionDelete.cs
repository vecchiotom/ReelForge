using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReelForge.WorkflowEngine.Migrations
{
    /// <inheritdoc />
    public partial class CascadeProjectExecutionDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_workflow_executions_projects_project_id",
                table: "workflow_executions");

            migrationBuilder.AddForeignKey(
                name: "fk_workflow_executions_projects_project_id",
                table: "workflow_executions",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_workflow_executions_projects_project_id",
                table: "workflow_executions");

            migrationBuilder.AddForeignKey(
                name: "fk_workflow_executions_projects_project_id",
                table: "workflow_executions",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
