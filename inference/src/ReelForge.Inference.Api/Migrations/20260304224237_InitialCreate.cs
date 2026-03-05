using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReelForge.Inference.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Use PostgreSQL "IF NOT EXISTS" statements so the migration is resilient
            // if the tables or indexes already exist.
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS application_users (
    id uuid NOT NULL,
    email text NOT NULL,
    display_name text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    password_hash text NOT NULL,
    is_admin boolean NOT NULL,
    must_change_password boolean NOT NULL,
    CONSTRAINT pk_application_users PRIMARY KEY (id)
);");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS agent_definitions (
    id uuid NOT NULL,
    name text NOT NULL,
    description text NOT NULL,
    system_prompt text NOT NULL,
    agent_type text NOT NULL,
    is_built_in boolean NOT NULL,
    owner_id uuid,
    config_json jsonb,
    created_at timestamp with time zone NOT NULL,
    color text,
    CONSTRAINT pk_agent_definitions PRIMARY KEY (id),
    CONSTRAINT fk_agent_definitions__application_users_owner_id FOREIGN KEY (owner_id)
        REFERENCES application_users (id) ON DELETE SET NULL
);");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS projects (
    id uuid NOT NULL,
    name text NOT NULL,
    description text,
    owner_id uuid NOT NULL,
    status text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_projects PRIMARY KEY (id),
    CONSTRAINT fk_projects_application_users_owner_id FOREIGN KEY (owner_id)
        REFERENCES application_users (id) ON DELETE CASCADE
);");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS project_files (
    id uuid NOT NULL,
    project_id uuid NOT NULL,
    original_file_name text NOT NULL,
    storage_key text NOT NULL,
    mime_type text NOT NULL,
    size_bytes bigint NOT NULL,
    agent_summary text,
    summary_status text NOT NULL,
    uploaded_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_project_files PRIMARY KEY (id),
    CONSTRAINT fk_project_files_projects_project_id FOREIGN KEY (project_id)
        REFERENCES projects (id) ON DELETE CASCADE
);");

            // Create indexes only if they don't already exist
            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS ix_agent_definitions_owner_id ON agent_definitions (owner_id);");

            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX IF NOT EXISTS IX_application_users_email ON application_users (email);");

            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS ix_project_files_project_id ON project_files (project_id);");

            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS ix_projects_owner_id ON projects (owner_id);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop tables only if they exist. Use CASCADE so dependent FKs and objects are removed safely.
            migrationBuilder.Sql("DROP TABLE IF EXISTS agent_definitions CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS project_files CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS projects CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS application_users CASCADE;");
        }
    }
}