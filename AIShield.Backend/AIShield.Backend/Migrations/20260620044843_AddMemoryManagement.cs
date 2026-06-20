using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIShield.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddMemoryManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MemoryRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgentId = table.Column<long>(type: "bigint", nullable: false),
                    ExternalMemoryId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MemoryKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Confidence = table.Column<double>(type: "float", nullable: false),
                    EmbeddingJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MemoryCreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastPositiveRef = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastDecayAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    ArchiveReason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemoryRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemoryRecords_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MemorySyncActions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentId = table.Column<long>(type: "bigint", nullable: false),
                    ExternalMemoryId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActionType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    NewConfidence = table.Column<double>(type: "float", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    FailedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemorySyncActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemorySyncActions_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryRecords_AgentId_ContentHash",
                table: "MemoryRecords",
                columns: new[] { "AgentId", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryRecords_AgentId_ExternalMemoryId",
                table: "MemoryRecords",
                columns: new[] { "AgentId", "ExternalMemoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemoryRecords_AgentId_IsArchived_UpdatedAt",
                table: "MemoryRecords",
                columns: new[] { "AgentId", "IsArchived", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryRecords_AgentId_Source_MemoryKey_IsArchived",
                table: "MemoryRecords",
                columns: new[] { "AgentId", "Source", "MemoryKey", "IsArchived" });

            migrationBuilder.CreateIndex(
                name: "IX_MemorySyncActions_ActionId",
                table: "MemorySyncActions",
                column: "ActionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemorySyncActions_AgentId_ExternalMemoryId_CreatedAt",
                table: "MemorySyncActions",
                columns: new[] { "AgentId", "ExternalMemoryId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MemorySyncActions_AgentId_Status_Id",
                table: "MemorySyncActions",
                columns: new[] { "AgentId", "Status", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemoryRecords");

            migrationBuilder.DropTable(
                name: "MemorySyncActions");
        }
    }
}
