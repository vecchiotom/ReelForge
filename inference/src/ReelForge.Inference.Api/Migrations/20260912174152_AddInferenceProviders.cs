using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReelForge.Inference.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInferenceProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "inference_provider_id",
                table: "agent_definitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "inference_providers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    endpoint = table.Column<string>(type: "text", nullable: false),
                    model_name = table.Column<string>(type: "text", nullable: false),
                    api_key_encrypted = table.Column<string>(type: "text", nullable: true),
                    api_key_last_four = table.Column<string>(type: "text", nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    timeout_seconds = table.Column<int>(type: "integer", nullable: true),
                    extra_headers_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_test_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_test_ok = table.Column<bool>(type: "boolean", nullable: true),
                    last_test_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inference_providers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_definitions_inference_provider_id",
                table: "agent_definitions",
                column: "inference_provider_id");

            migrationBuilder.CreateIndex(
                name: "IX_inference_providers_is_default",
                table: "inference_providers",
                column: "is_default",
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "IX_inference_providers_name",
                table: "inference_providers",
                column: "name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_agent_definitions__inference_providers_inference_provider_id",
                table: "agent_definitions",
                column: "inference_provider_id",
                principalTable: "inference_providers",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_agent_definitions__inference_providers_inference_provider_id",
                table: "agent_definitions");

            migrationBuilder.DropTable(
                name: "inference_providers");

            migrationBuilder.DropIndex(
                name: "ix_agent_definitions_inference_provider_id",
                table: "agent_definitions");

            migrationBuilder.DropColumn(
                name: "inference_provider_id",
                table: "agent_definitions");
        }
    }
}
