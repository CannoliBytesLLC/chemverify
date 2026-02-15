using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aegis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRunParamsEvidenceRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EvidenceRef",
                table: "ValidationFindings",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConnectorName",
                table: "AiRuns",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModelVersion",
                table: "AiRuns",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParametersJson",
                table: "AiRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyProfile",
                table: "AiRuns",
                type: "TEXT",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EvidenceRef",
                table: "ValidationFindings");

            migrationBuilder.DropColumn(
                name: "ConnectorName",
                table: "AiRuns");

            migrationBuilder.DropColumn(
                name: "ModelVersion",
                table: "AiRuns");

            migrationBuilder.DropColumn(
                name: "ParametersJson",
                table: "AiRuns");

            migrationBuilder.DropColumn(
                name: "PolicyProfile",
                table: "AiRuns");
        }
    }
}
