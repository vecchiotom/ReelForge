using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReelForge.Inference.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentContextSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "context_mode",
                table: "agent_definitions",
                type: "text",
                nullable: false,
                defaultValue: "LastStep");

            migrationBuilder.AddColumn<int>(
                name: "context_window_size",
                table: "agent_definitions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "context_mode",
                table: "agent_definitions");

            migrationBuilder.DropColumn(
                name: "context_window_size",
                table: "agent_definitions");
        }
    }
}
