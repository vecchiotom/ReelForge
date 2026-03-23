using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ReelForge.Inference.Api.Data;

#nullable disable

namespace ReelForge.Inference.Api.Migrations
{
    [DbContext(typeof(InferenceApiDbContext))]
    [Migration("20260322120000_AddProjectFileIndexingStatus")]
    public partial class AddProjectFileIndexingStatus : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "indexed_at",
                table: "project_files",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "indexing_error",
                table: "project_files",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "indexing_status",
                table: "project_files",
                type: "text",
                nullable: false,
                defaultValue: "NotIndexed");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "indexed_at",
                table: "project_files");

            migrationBuilder.DropColumn(
                name: "indexing_error",
                table: "project_files");

            migrationBuilder.DropColumn(
                name: "indexing_status",
                table: "project_files");
        }
    }
}
