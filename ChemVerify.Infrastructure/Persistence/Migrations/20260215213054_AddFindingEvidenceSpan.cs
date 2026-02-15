using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChemVerify.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFindingEvidenceSpan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EvidenceEndOffset",
                table: "ValidationFindings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceEntityKey",
                table: "ValidationFindings",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceSnippet",
                table: "ValidationFindings",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EvidenceStartOffset",
                table: "ValidationFindings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EvidenceStepIndex",
                table: "ValidationFindings",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EvidenceEndOffset",
                table: "ValidationFindings");

            migrationBuilder.DropColumn(
                name: "EvidenceEntityKey",
                table: "ValidationFindings");

            migrationBuilder.DropColumn(
                name: "EvidenceSnippet",
                table: "ValidationFindings");

            migrationBuilder.DropColumn(
                name: "EvidenceStartOffset",
                table: "ValidationFindings");

            migrationBuilder.DropColumn(
                name: "EvidenceStepIndex",
                table: "ValidationFindings");
        }
    }
}
