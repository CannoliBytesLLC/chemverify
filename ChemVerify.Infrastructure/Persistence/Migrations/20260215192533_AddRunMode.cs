using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChemVerify.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRunMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "AiRuns",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Mode",
                table: "AiRuns");
        }
    }
}
