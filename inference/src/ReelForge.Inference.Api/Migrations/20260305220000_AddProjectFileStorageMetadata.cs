using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReelForge.Inference.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectFileStorageMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE IF EXISTS project_files
    ADD COLUMN IF NOT EXISTS storage_bucket text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS storage_prefix text NULL,
    ADD COLUMN IF NOT EXISTS storage_metadata_json jsonb NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE IF EXISTS project_files
    DROP COLUMN IF EXISTS storage_metadata_json,
    DROP COLUMN IF EXISTS storage_prefix,
    DROP COLUMN IF EXISTS storage_bucket;");
        }
    }
}
