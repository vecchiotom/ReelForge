using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReelForge.WorkflowEngine.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeWorkflowNumericColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
BEGIN
    -- workflow_steps
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_name = 'workflow_steps'
          AND column_name = 'step_order'
          AND data_type IN ('text', 'character varying')
    ) THEN
        EXECUTE 'ALTER TABLE workflow_steps ALTER COLUMN step_order TYPE integer USING CASE WHEN trim(step_order) ~ ''^-?\\d+$'' THEN step_order::integer ELSE 0 END';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_name = 'workflow_steps'
          AND column_name = 'loop_target_step_order'
          AND data_type IN ('text', 'character varying')
    ) THEN
        EXECUTE 'ALTER TABLE workflow_steps ALTER COLUMN loop_target_step_order TYPE integer USING CASE WHEN loop_target_step_order IS NULL OR trim(loop_target_step_order) = '''' THEN NULL WHEN trim(loop_target_step_order) ~ ''^-?\\d+$'' THEN loop_target_step_order::integer ELSE NULL END';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_name = 'workflow_steps'
          AND column_name = 'max_iterations'
          AND data_type IN ('text', 'character varying')
    ) THEN
        EXECUTE 'ALTER TABLE workflow_steps ALTER COLUMN max_iterations TYPE integer USING CASE WHEN trim(max_iterations) ~ ''^-?\\d+$'' THEN max_iterations::integer ELSE 3 END';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_name = 'workflow_steps'
          AND column_name = 'min_score'
          AND data_type IN ('text', 'character varying')
    ) THEN
        EXECUTE 'ALTER TABLE workflow_steps ALTER COLUMN min_score TYPE integer USING CASE WHEN min_score IS NULL OR trim(min_score) = '''' THEN NULL WHEN trim(min_score) ~ ''^-?\\d+$'' THEN min_score::integer ELSE NULL END';
    END IF;

    -- workflow_executions
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_name = 'workflow_executions'
          AND column_name = 'iteration_count'
          AND data_type IN ('text', 'character varying')
    ) THEN
        EXECUTE 'ALTER TABLE workflow_executions ALTER COLUMN iteration_count TYPE integer USING CASE WHEN trim(iteration_count) ~ ''^-?\\d+$'' THEN iteration_count::integer ELSE 0 END';
    END IF;

    -- workflow_step_results
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_name = 'workflow_step_results'
          AND column_name = 'tokens_used'
          AND data_type IN ('text', 'character varying')
    ) THEN
        EXECUTE 'ALTER TABLE workflow_step_results ALTER COLUMN tokens_used TYPE integer USING CASE WHEN trim(tokens_used) ~ ''^-?\\d+$'' THEN tokens_used::integer ELSE 0 END';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_name = 'workflow_step_results'
          AND column_name = 'duration_ms'
          AND data_type IN ('text', 'character varying')
    ) THEN
        EXECUTE 'ALTER TABLE workflow_step_results ALTER COLUMN duration_ms TYPE bigint USING CASE WHEN trim(duration_ms) ~ ''^-?\\d+$'' THEN duration_ms::bigint ELSE 0 END';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_name = 'workflow_step_results'
          AND column_name = 'iteration_number'
          AND data_type IN ('text', 'character varying')
    ) THEN
        EXECUTE 'ALTER TABLE workflow_step_results ALTER COLUMN iteration_number TYPE integer USING CASE WHEN iteration_number IS NULL OR trim(iteration_number) = '''' THEN NULL WHEN trim(iteration_number) ~ ''^-?\\d+$'' THEN iteration_number::integer ELSE NULL END';
    END IF;

    -- review_scores
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_name = 'review_scores'
          AND column_name = 'iteration_number'
          AND data_type IN ('text', 'character varying')
    ) THEN
        EXECUTE 'ALTER TABLE review_scores ALTER COLUMN iteration_number TYPE integer USING CASE WHEN trim(iteration_number) ~ ''^-?\\d+$'' THEN iteration_number::integer ELSE 0 END';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_name = 'review_scores'
          AND column_name = 'score'
          AND data_type IN ('text', 'character varying')
    ) THEN
        EXECUTE 'ALTER TABLE review_scores ALTER COLUMN score TYPE integer USING CASE WHEN trim(score) ~ ''^-?\\d+$'' THEN score::integer ELSE 0 END';
    END IF;
END $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible data normalization migration.
        }
    }
}
