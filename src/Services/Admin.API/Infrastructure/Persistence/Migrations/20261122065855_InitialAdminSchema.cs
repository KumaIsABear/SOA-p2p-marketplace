using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Admin.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAdminSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HttpIdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ResourceScope = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    ResponseBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HttpIdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Market_Daily_Summaries",
                columns: table => new
                {
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TradeCount = table.Column<long>(type: "bigint", nullable: false),
                    TotalGrossGem = table.Column<long>(type: "bigint", nullable: false),
                    TotalFeeGem = table.Column<long>(type: "bigint", nullable: false),
                    Volume = table.Column<long>(type: "bigint", nullable: false),
                    MinUnitPrice = table.Column<long>(type: "bigint", nullable: true),
                    MaxUnitPrice = table.Column<long>(type: "bigint", nullable: true),
                    ReferencePrice = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Market_Daily_Summaries", x => new { x.Day, x.TemplateId });
                    table.CheckConstraint("CK_Market_Daily_Summaries_ReferencePrice_Positive", "[ReferencePrice] >= 1");
                });

            migrationBuilder.CreateTable(
                name: "Moderation_Actions",
                columns: table => new
                {
                    ActionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Moderation_Actions", x => x.ActionId);
                });

            migrationBuilder.CreateTable(
                name: "Price_Policies",
                columns: table => new
                {
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BasePrice = table.Column<long>(type: "bigint", nullable: false),
                    BandBasisPoints = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Price_Policies", x => x.TemplateId);
                    table.CheckConstraint("CK_Price_Policies_BandBasisPoints_Range", "[BandBasisPoints] >= 0 AND [BandBasisPoints] <= 9999");
                    table.CheckConstraint("CK_Price_Policies_BasePrice_Positive", "[BasePrice] >= 1");
                    table.CheckConstraint("CK_Price_Policies_Version_Positive", "[Version] >= 1");
                });

            migrationBuilder.CreateTable(
                name: "Trade_Facts",
                columns: table => new
                {
                    TradeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrossGem = table.Column<long>(type: "bigint", nullable: false),
                    FeeGem = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trade_Facts", x => x.TradeId);
                    table.CheckConstraint("CK_Trade_Facts_FeeGem_NonNegative", "[FeeGem] >= 0");
                    table.CheckConstraint("CK_Trade_Facts_GrossGem_Positive", "[GrossGem] >= 1");
                    table.CheckConstraint("CK_Trade_Facts_Quantity_Positive", "[Quantity] >= 1");
                });

            migrationBuilder.CreateTable(
                name: "Price_Policy_Audits",
                columns: table => new
                {
                    AuditId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BasePrice = table.Column<long>(type: "bigint", nullable: false),
                    BandBasisPoints = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Price_Policy_Audits", x => x.AuditId);
                    table.ForeignKey(
                        name: "FK_Price_Policy_Audits_Price_Policies_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "Price_Policies",
                        principalColumn: "TemplateId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_HttpIdempotencyRecords_ScopeKey",
                table: "HttpIdempotencyRecords",
                columns: new[] { "ActorId", "Operation", "ResourceScope", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Market_Daily_Summaries_TemplateId_Day",
                table: "Market_Daily_Summaries",
                columns: new[] { "TemplateId", "Day" });

            migrationBuilder.CreateIndex(
                name: "IX_Moderation_Actions_OperationId",
                table: "Moderation_Actions",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Moderation_Actions_TargetUserId",
                table: "Moderation_Actions",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Price_Policy_Audits_TemplateId_CreatedAt",
                table: "Price_Policy_Audits",
                columns: new[] { "TemplateId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Trade_Facts_CompletedAt",
                table: "Trade_Facts",
                column: "CompletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Trade_Facts_TemplateId_CompletedAt",
                table: "Trade_Facts",
                columns: new[] { "TemplateId", "CompletedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HttpIdempotencyRecords");

            migrationBuilder.DropTable(
                name: "Market_Daily_Summaries");

            migrationBuilder.DropTable(
                name: "Moderation_Actions");

            migrationBuilder.DropTable(
                name: "Price_Policy_Audits");

            migrationBuilder.DropTable(
                name: "Trade_Facts");

            migrationBuilder.DropTable(
                name: "Price_Policies");
        }
    }
}
