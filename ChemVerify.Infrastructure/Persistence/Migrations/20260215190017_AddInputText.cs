using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChemVerify.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInputText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InputText",
                table: "AiRuns",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InputText",
                table: "AiRuns");
        }
    }
}
