using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReelForge.Inference.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectFileTreeMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_project_files_project_id",
                table: "project_files");

            migrationBuilder.AddColumn<string>(
                name: "directory_path",
                table: "project_files",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "storage_file_name",
                table: "project_files",
                type: "character varying(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_files_project_id_category_directory_path",
                table: "project_files",
                columns: new[] { "project_id", "category", "directory_path" });

            migrationBuilder.CreateIndex(
                name: "IX_project_files_project_id_category_uploaded_at",
                table: "project_files",
                columns: new[] { "project_id", "category", "uploaded_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_project_files_project_id_category_directory_path",
                table: "project_files");

            migrationBuilder.DropIndex(
                name: "IX_project_files_project_id_category_uploaded_at",
                table: "project_files");

            migrationBuilder.DropColumn(
                name: "directory_path",
                table: "project_files");

            migrationBuilder.DropColumn(
                name: "storage_file_name",
                table: "project_files");

            migrationBuilder.CreateIndex(
                name: "ix_project_files_project_id",
                table: "project_files",
                column: "project_id");
        }
    }
}
