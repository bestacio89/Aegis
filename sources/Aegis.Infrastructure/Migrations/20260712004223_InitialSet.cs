using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aegis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ScanId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    ProjectPath = table.Column<string>(type: "text", nullable: false),
                    Language = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Framework = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ScanDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AnalyzerVersion = table.Column<string>(type: "text", nullable: true),
                    RuleSetVersion = table.Column<string>(type: "text", nullable: true),
                    ArchitectureStyle = table.Column<string>(type: "text", nullable: true),
                    DomainType = table.Column<string>(type: "text", nullable: true),
                    DetectionStrategy = table.Column<string>(type: "text", nullable: true),
                    DetectionConfidence = table.Column<double>(type: "double precision", nullable: false),
                    FileCount = table.Column<int>(type: "integer", nullable: false),
                    FactCount = table.Column<int>(type: "integer", nullable: false),
                    TotalViolations = table.Column<int>(type: "integer", nullable: false),
                    DomainCount = table.Column<int>(type: "integer", nullable: false),
                    HealthIndex = table.Column<double>(type: "double precision", precision: 5, scale: 2, nullable: false),
                    WeightedCompliance = table.Column<double>(type: "double precision", precision: 5, scale: 2, nullable: false),
                    MaintainabilityIndex = table.Column<double>(type: "double precision", nullable: false),
                    ResilienceIndex = table.Column<double>(type: "double precision", nullable: false),
                    ReportJson = table.Column<string>(type: "text", nullable: false),
                    ContextJson = table.Column<string>(type: "text", nullable: true),
                    PolicyJson = table.Column<string>(type: "text", nullable: true),
                    DateCreated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DateDeleted = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RuleResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReportId = table.Column<int>(type: "integer", nullable: false),
                    ScanId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RuleName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RuleVersion = table.Column<string>(type: "text", nullable: true),
                    EvaluatorName = table.Column<string>(type: "text", nullable: false),
                    AnalyzerVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Severity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Domain = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Target = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SourceFile = table.Column<string>(type: "text", nullable: true),
                    SourceLayer = table.Column<string>(type: "text", nullable: true),
                    SourceModule = table.Column<string>(type: "text", nullable: true),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IsCompliant = table.Column<bool>(type: "boolean", nullable: false),
                    EvidenceJson = table.Column<string>(type: "text", nullable: true),
                    ImpactScore = table.Column<double>(type: "double precision", precision: 5, scale: 2, nullable: false),
                    WeightFactor = table.Column<double>(type: "double precision", nullable: false),
                    WeightedImpact = table.Column<double>(type: "double precision", precision: 5, scale: 2, nullable: false),
                    DateDetected = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Project = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    DateCreated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DateDeleted = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RuleResults_Reports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RuleResults_ReportId",
                table: "RuleResults",
                column: "ReportId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RuleResults");

            migrationBuilder.DropTable(
                name: "Reports");
        }
    }
}
