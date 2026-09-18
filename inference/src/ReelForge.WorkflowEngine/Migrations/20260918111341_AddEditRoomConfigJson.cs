using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReelForge.WorkflowEngine.Migrations
{
    /// <inheritdoc />
    public partial class AddEditRoomConfigJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "edit_room_config_json",
                table: "workflow_steps",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "edit_room_config_json",
                table: "workflow_steps");
        }
    }
}
