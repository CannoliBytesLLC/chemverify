using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChemVerify.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddValidationFindingJsonPayload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JsonPayload",
                table: "ValidationFindings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JsonPayload",
                table: "ValidationFindings");
        }
    }
}
