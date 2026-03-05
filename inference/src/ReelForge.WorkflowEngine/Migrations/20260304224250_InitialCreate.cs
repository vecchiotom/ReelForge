using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReelForge.WorkflowEngine.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workflow_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_definitions", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_definitions_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_order = table.Column<int>(type: "integer", nullable: false),
                    edge_condition_json = table.Column<string>(type: "jsonb", nullable: true),
                    label = table.Column<string>(type: "text", nullable: true),
                    step_type = table.Column<string>(type: "text", nullable: false),
                    condition_expression = table.Column<string>(type: "text", nullable: true),
                    loop_source_expression = table.Column<string>(type: "text", nullable: true),
                    loop_target_step_order = table.Column<int>(type: "integer", nullable: true),
                    max_iterations = table.Column<int>(type: "integer", nullable: false),
                    min_score = table.Column<int>(type: "integer", nullable: true),
                    input_mapping_json = table.Column<string>(type: "jsonb", nullable: true),
                    true_branch_step_order = table.Column<string>(type: "text", nullable: true),
                    false_branch_step_order = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_steps_agent_definitions_agent_definition_id",
                        column: x => x.agent_definition_id,
                        principalTable: "agent_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workflow_steps_workflow_definitions_workflow_definition_id",
                        column: x => x.workflow_definition_id,
                        principalTable: "workflow_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_executions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    current_step_id = table.Column<Guid>(type: "uuid", nullable: true),
                    iteration_count = table.Column<int>(type: "integer", nullable: false),
                    result_json = table.Column<string>(type: "jsonb", nullable: true),
                    correlation_id = table.Column<string>(type: "text", nullable: false),
                    initiated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_executions", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_executions__workflow_steps_current_step_id",
                        column: x => x.current_step_id,
                        principalTable: "workflow_steps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_workflow_executions_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workflow_executions_workflow_definitions_workflow_definitio~",
                        column: x => x.workflow_definition_id,
                        principalTable: "workflow_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_scores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    iteration_number = table.Column<int>(type: "integer", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    comments = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_scores", x => x.id);
                    table.ForeignKey(
                        name: "fk_review_scores__workflow_executions_workflow_execution_id",
                        column: x => x.workflow_execution_id,
                        principalTable: "workflow_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_step_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    output = table.Column<string>(type: "text", nullable: false),
                    tokens_used = table.Column<int>(type: "integer", nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    executed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    input_json = table.Column<string>(type: "jsonb", nullable: true),
                    output_json = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    error_details = table.Column<string>(type: "text", nullable: true),
                    iteration_number = table.Column<int>(type: "integer", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_step_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_step_results_workflow_executions_workflow_executio~",
                        column: x => x.workflow_execution_id,
                        principalTable: "workflow_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_workflow_step_results_workflow_steps_workflow_step_id",
                        column: x => x.workflow_step_id,
                        principalTable: "workflow_steps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_review_scores_workflow_execution_id",
                table: "review_scores",
                column: "workflow_execution_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_definitions_project_id",
                table: "workflow_definitions",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_executions_current_step_id",
                table: "workflow_executions",
                column: "current_step_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_executions_project_id",
                table: "workflow_executions",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_executions_workflow_definition_id",
                table: "workflow_executions",
                column: "workflow_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_step_results_workflow_execution_id",
                table: "workflow_step_results",
                column: "workflow_execution_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_step_results_workflow_step_id",
                table: "workflow_step_results",
                column: "workflow_step_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_steps_agent_definition_id",
                table: "workflow_steps",
                column: "agent_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_steps_workflow_definition_id",
                table: "workflow_steps",
                column: "workflow_definition_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "review_scores");

            migrationBuilder.DropTable(
                name: "workflow_step_results");

            migrationBuilder.DropTable(
                name: "workflow_executions");

            migrationBuilder.DropTable(
                name: "workflow_steps");

            migrationBuilder.DropTable(
                name: "workflow_definitions");
        }
    }
}
