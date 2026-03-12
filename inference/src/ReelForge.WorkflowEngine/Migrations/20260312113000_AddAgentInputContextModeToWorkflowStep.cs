using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReelForge.WorkflowEngine.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentInputContextModeToWorkflowStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider.Contains("Sqlite"))
            {
                migrationBuilder.Sql(@"
ALTER TABLE workflow_steps
    ADD COLUMN agent_input_context_mode TEXT;");

                migrationBuilder.Sql(@"
ALTER TABLE workflow_steps
    ADD COLUMN selected_prior_step_orders_json TEXT;");
            }
            else
            {
                migrationBuilder.Sql(@"
ALTER TABLE workflow_steps
    ADD COLUMN IF NOT EXISTS agent_input_context_mode text;");

                migrationBuilder.Sql(@"
ALTER TABLE workflow_steps
    ADD COLUMN IF NOT EXISTS selected_prior_step_orders_json jsonb;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider.Contains("Sqlite"))
            {
                migrationBuilder.Sql(@"
ALTER TABLE workflow_steps
    DROP COLUMN selected_prior_step_orders_json;");

                migrationBuilder.Sql(@"
ALTER TABLE workflow_steps
    DROP COLUMN agent_input_context_mode;");
            }
            else
            {
                migrationBuilder.Sql(@"
ALTER TABLE workflow_steps
    DROP COLUMN IF EXISTS selected_prior_step_orders_json;");

                migrationBuilder.Sql(@"
ALTER TABLE workflow_steps
    DROP COLUMN IF EXISTS agent_input_context_mode;");
            }
        }
    }
}
