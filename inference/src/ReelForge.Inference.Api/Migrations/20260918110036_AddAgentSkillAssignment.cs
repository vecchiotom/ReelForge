using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReelForge.Inference.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentSkillAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "assigned_skills_json",
                table: "agent_definitions",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "assigned_skills_json",
                table: "agent_definitions");
        }
    }
}
