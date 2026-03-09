using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReelForge.Inference.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectFileCategoryAndPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "original_path",
                table: "project_files",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "project_files",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "userFiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "original_path",
                table: "project_files");

            migrationBuilder.DropColumn(
                name: "category",
                table: "project_files");
        }
    }
}
