using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReelForge.Inference.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentCapabilityMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "available_tools_json",
                table: "agent_definitions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "generates_output",
                table: "agent_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "output_schema_name",
                table: "agent_definitions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "available_tools_json",
                table: "agent_definitions");

            migrationBuilder.DropColumn(
                name: "generates_output",
                table: "agent_definitions");

            migrationBuilder.DropColumn(
                name: "output_schema_name",
                table: "agent_definitions");
        }
    }
}
