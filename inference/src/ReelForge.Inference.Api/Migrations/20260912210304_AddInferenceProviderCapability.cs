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

            migrationBuilder.CreateIndex(
                name: "IX_inference_providers_is_default",
                table: "inference_providers",
                column: "is_default",
                unique: true,
                filter: "is_default");
        }
    }
}
