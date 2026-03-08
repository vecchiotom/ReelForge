using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReelForge.WorkflowEngine.Migrations
{
    /// <inheritdoc />
    public partial class AddParallelAgentIdsToWorkflowStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE workflow_steps
    ADD COLUMN IF NOT EXISTS parallel_agent_ids_json jsonb;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE workflow_steps
    DROP COLUMN IF EXISTS parallel_agent_ids_json;");
        }
    }
}
