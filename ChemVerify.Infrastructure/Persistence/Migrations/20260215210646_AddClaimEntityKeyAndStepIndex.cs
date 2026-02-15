using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChemVerify.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClaimEntityKeyAndStepIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EntityKey",
                table: "ExtractedClaims",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StepIndex",
                table: "ExtractedClaims",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EntityKey",
                table: "ExtractedClaims");

            migrationBuilder.DropColumn(
                name: "StepIndex",
                table: "ExtractedClaims");
        }
    }
}
