using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReelForge.WorkflowEngine.Migrations
{
    /// <inheritdoc />
    public partial class CascadeStepResultDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_workflow_step_results_workflow_steps_workflow_step_id",
                table: "workflow_step_results");

            migrationBuilder.AddForeignKey(
                name: "fk_workflow_step_results_workflow_steps_workflow_step_id",
                table: "workflow_step_results",
                column: "workflow_step_id",
                principalTable: "workflow_steps",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_workflow_step_results_workflow_steps_workflow_step_id",
                table: "workflow_step_results");

            migrationBuilder.AddForeignKey(
                name: "fk_workflow_step_results_workflow_steps_workflow_step_id",
                table: "workflow_step_results",
                column: "workflow_step_id",
                principalTable: "workflow_steps",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
