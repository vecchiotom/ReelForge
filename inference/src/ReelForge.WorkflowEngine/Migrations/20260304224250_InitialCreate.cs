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
            // Create tables using raw SQL with IF NOT EXISTS so migration is resilient
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS workflow_definitions (
    id uuid NOT NULL,
    name text NOT NULL,
    project_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_workflow_definitions PRIMARY KEY (id),
    CONSTRAINT fk_workflow_definitions_projects_project_id FOREIGN KEY (project_id)
        REFERENCES projects (id) ON DELETE CASCADE
);");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS workflow_steps (
    id uuid NOT NULL,
    workflow_definition_id uuid NOT NULL,
    agent_definition_id uuid NOT NULL,
    step_order integer NOT NULL,
    edge_condition_json jsonb,
    label text,
    step_type text NOT NULL,
    condition_expression text,
    loop_source_expression text,
    loop_target_step_order integer,
    max_iterations integer NOT NULL,
    min_score integer,
    input_mapping_json jsonb,
    true_branch_step_order text,
    false_branch_step_order text,
    CONSTRAINT pk_workflow_steps PRIMARY KEY (id),
    CONSTRAINT fk_workflow_steps_agent_definitions_agent_definition_id FOREIGN KEY (agent_definition_id)
        REFERENCES agent_definitions (id) ON DELETE RESTRICT,
    CONSTRAINT fk_workflow_steps_workflow_definitions_workflow_definition_id FOREIGN KEY (workflow_definition_id)
        REFERENCES workflow_definitions (id) ON DELETE CASCADE
);");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS workflow_executions (
    id uuid NOT NULL,
    workflow_definition_id uuid NOT NULL,
    project_id uuid NOT NULL,
    status text NOT NULL,
    started_at timestamp with time zone,
    completed_at timestamp with time zone,
    current_step_id uuid,
    iteration_count integer NOT NULL,
    result_json jsonb,
    correlation_id text NOT NULL,
    initiated_by_user_id uuid,
    error_message text,
    CONSTRAINT pk_workflow_executions PRIMARY KEY (id),
    CONSTRAINT fk_workflow_executions__workflow_steps_current_step_id FOREIGN KEY (current_step_id)
        REFERENCES workflow_steps (id) ON DELETE SET NULL,
    CONSTRAINT fk_workflow_executions_projects_project_id FOREIGN KEY (project_id)
        REFERENCES projects (id) ON DELETE RESTRICT,
    CONSTRAINT fk_workflow_executions_workflow_definitions_workflow_definition_id FOREIGN KEY (workflow_definition_id)
        REFERENCES workflow_definitions (id) ON DELETE CASCADE
);");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS review_scores (
    id uuid NOT NULL,
    workflow_execution_id uuid NOT NULL,
    iteration_number integer NOT NULL,
    score integer NOT NULL,
    comments text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_review_scores PRIMARY KEY (id),
    CONSTRAINT fk_review_scores__workflow_executions_workflow_execution_id FOREIGN KEY (workflow_execution_id)
        REFERENCES workflow_executions (id) ON DELETE CASCADE
);");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS workflow_step_results (
    id uuid NOT NULL,
    workflow_execution_id uuid NOT NULL,
    workflow_step_id uuid NOT NULL,
    output text NOT NULL,
    tokens_used integer NOT NULL,
    duration_ms bigint NOT NULL,
    executed_at timestamp with time zone NOT NULL,
    input_json jsonb,
    output_json jsonb,
    status text NOT NULL,
    error_details text,
    iteration_number integer,
    completed_at timestamp with time zone,
    CONSTRAINT pk_workflow_step_results PRIMARY KEY (id),
    CONSTRAINT fk_workflow_step_results_workflow_executions_workflow_execution_id FOREIGN KEY (workflow_execution_id)
        REFERENCES workflow_executions (id) ON DELETE CASCADE,
    CONSTRAINT fk_workflow_step_results_workflow_steps_workflow_step_id FOREIGN KEY (workflow_step_id)
        REFERENCES workflow_steps (id) ON DELETE RESTRICT
);");

            // Create indexes only if they don't already exist
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ix_review_scores_workflow_execution_id ON review_scores (workflow_execution_id);");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ix_workflow_definitions_project_id ON workflow_definitions (project_id);");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ix_workflow_executions_current_step_id ON workflow_executions (current_step_id);");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ix_workflow_executions_project_id ON workflow_executions (project_id);");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ix_workflow_executions_workflow_definition_id ON workflow_executions (workflow_definition_id);");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ix_workflow_step_results_workflow_execution_id ON workflow_step_results (workflow_execution_id);");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ix_workflow_step_results_workflow_step_id ON workflow_step_results (workflow_step_id);");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ix_workflow_steps_agent_definition_id ON workflow_steps (agent_definition_id);");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ix_workflow_steps_workflow_definition_id ON workflow_steps (workflow_definition_id);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop tables if they exist. Use CASCADE to remove dependent objects safely.
            migrationBuilder.Sql("DROP TABLE IF EXISTS review_scores CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS workflow_step_results CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS workflow_executions CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS workflow_steps CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS workflow_definitions CASCADE;");
        }
    }
}
