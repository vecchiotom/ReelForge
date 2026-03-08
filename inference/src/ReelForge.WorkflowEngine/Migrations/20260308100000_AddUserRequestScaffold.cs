using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReelForge.WorkflowEngine.Migrations
{
    /// <inheritdoc />
    public partial class AddUserRequestScaffold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE workflow_definitions
    ADD COLUMN IF NOT EXISTS requires_user_input boolean NOT NULL DEFAULT false;");

            migrationBuilder.Sql(@"
ALTER TABLE workflow_executions
    ADD COLUMN IF NOT EXISTS user_request text;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE workflow_definitions
    DROP COLUMN IF EXISTS requires_user_input;");

            migrationBuilder.Sql(@"
ALTER TABLE workflow_executions
    DROP COLUMN IF EXISTS user_request;");
        }
    }
}
