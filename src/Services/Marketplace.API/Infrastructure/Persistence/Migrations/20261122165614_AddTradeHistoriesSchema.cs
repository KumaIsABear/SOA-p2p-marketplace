using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeHistoriesSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Trade_Histories",
                columns: table => new
                {
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
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RequiresReview = table.Column<bool>(type: "bit", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trade_Histories", x => x.TradeId);
                    table.ForeignKey(
                        name: "FK_Trade_Histories_Listings_ListingId",
                        column: x => x.ListingId,
                        principalTable: "Listings",
                        principalColumn: "ListingId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Trade_Histories_BuyerId_IdempotencyKey",
                table: "Trade_Histories",
                columns: new[] { "BuyerId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trade_Histories_BuyerId_Status_CreatedAt",
                table: "Trade_Histories",
                columns: new[] { "BuyerId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Trade_Histories_ListingId",
                table: "Trade_Histories",
                column: "ListingId",
                unique: true,
                filter: "[Status] = 'Completed'");

            migrationBuilder.CreateIndex(
                name: "IX_Trade_Histories_SellerId_Status_CreatedAt",
                table: "Trade_Histories",
                columns: new[] { "SellerId", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Trade_Histories");
        }
    }
}
