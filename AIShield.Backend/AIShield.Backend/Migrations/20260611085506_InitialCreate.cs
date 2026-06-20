using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIShield.Backend.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Agents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgentName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Scenario = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    AgentKeyHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AgentKeyFingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AgentKeyPreview = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AgentKeySalt = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgentId = table.Column<long>(type: "bigint", nullable: false),
                    AgentName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SubjectHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OriginalContent = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProcessedContent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RiskLevel = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    HitRules = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ClientIp = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecurityRules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RuleId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RuleType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MatchType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Pattern = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RiskLevel = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Replacement = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    IsSystemRule = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentRules",
                columns: table => new
                {
                    AgentId = table.Column<long>(type: "bigint", nullable: false),
                    RuleId = table.Column<long>(type: "bigint", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRules", x => new { x.AgentId, x.RuleId });
                    table.ForeignKey(
                        name: "FK_AgentRules_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AgentRules_SecurityRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "SecurityRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentRules_RuleId",
                table: "AgentRules",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Agents_AgentKeyFingerprint",
                table: "Agents",
                column: "AgentKeyFingerprint",
                unique: true,
                filter: "[AgentKeyFingerprint] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_AgentId",
                table: "AuditRecords",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_CreatedAt",
                table: "AuditRecords",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityRules_RuleId",
                table: "SecurityRules",
                column: "RuleId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentRules");

            migrationBuilder.DropTable(
                name: "AuditRecords");

            migrationBuilder.DropTable(
                name: "Agents");

            migrationBuilder.DropTable(
                name: "SecurityRules");
        }
    }
}
