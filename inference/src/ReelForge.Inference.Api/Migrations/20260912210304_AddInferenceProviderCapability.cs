using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReelForge.Inference.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInferenceProviderCapability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_inference_providers_is_default",
                table: "inference_providers");

            migrationBuilder.AddColumn<string>(
                name: "capability",
                table: "inference_providers",
                type: "text",
                nullable: false,
                defaultValue: "Chat");

            migrationBuilder.CreateIndex(
                name: "IX_inference_providers_capability_is_default",
                table: "inference_providers",
                columns: new[] { "capability", "is_default" },
                unique: true,
                filter: "is_default");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_inference_providers_capability_is_default",
                table: "inference_providers");

            migrationBuilder.DropColumn(
                name: "capability",
                table: "inference_providers");

            // After normal use of the capability split, the database can legitimately hold two
            // default rows (one Chat, one Transcription) — the composite index above allows
            // exactly that. The single-column unique index being recreated below cannot coexist
            // with more than one is_default=true row, so creating it would fail on any database
            // that has actually used this feature (found by Copilot review). Rollback is not
            // expected to preserve which provider was "the" default across two capabilities that
            // no longer exist post-rollback, so deterministically keep only the earliest-created
            // default and clear the rest before the index can enforce that invariant.
            migrationBuilder.Sql(@"
                UPDATE inference_providers
                SET is_default = false
                WHERE is_default = true
                  AND id NOT IN (
                    SELECT id FROM inference_providers
                    WHERE is_default = true
                    ORDER BY created_at
                    LIMIT 1
                  );
            ");

            migrationBuilder.CreateIndex(
                name: "IX_inference_providers_is_default",
                table: "inference_providers",
                column: "is_default",
                unique: true,
                filter: "is_default");
        }
    }
}
