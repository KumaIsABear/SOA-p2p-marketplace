using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeSagaSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Saga_States",
                columns: table => new
                {
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TradeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ListingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuyerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SellerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<long>(type: "bigint", nullable: false),
                    GrossGem = table.Column<long>(type: "bigint", nullable: false),
                    FeeGem = table.Column<long>(type: "bigint", nullable: false),
                    SellerNetGem = table.Column<long>(type: "bigint", nullable: false),
                    CurrentState = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CurrentOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    RequiresReview = table.Column<bool>(type: "bit", nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Saga_States", x => x.CorrelationId);
                    table.ForeignKey(
                        name: "FK_Saga_States_Trade_Histories_TradeId",
                        column: x => x.TradeId,
                        principalTable: "Trade_Histories",
                        principalColumn: "TradeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Saga_Steps",
                columns: table => new
                {
                    StepId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TradeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Step = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Saga_Steps", x => x.StepId);
                    table.ForeignKey(
                        name: "FK_Saga_Steps_Trade_Histories_TradeId",
                        column: x => x.TradeId,
                        principalTable: "Trade_Histories",
                        principalColumn: "TradeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Saga_States_CurrentState_UpdatedAt",
                table: "Saga_States",
                columns: new[] { "CurrentState", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Saga_States_TradeId",
                table: "Saga_States",
                column: "TradeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Saga_Steps_TradeId_At",
                table: "Saga_Steps",
                columns: new[] { "TradeId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Saga_States");

            migrationBuilder.DropTable(
                name: "Saga_Steps");
        }
    }
}
