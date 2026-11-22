using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HttpIdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResourceScope = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    ResponseBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LocationHeader = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HttpIdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Wallets",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TotalBalance = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    HoldBalance = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wallets", x => x.UserId);
                    table.CheckConstraint("CK_Wallets_HoldBalance_NonNegative", "[HoldBalance] >= 0");
                    table.CheckConstraint("CK_Wallets_TotalBalance_Gte_HoldBalance", "[TotalBalance] >= [HoldBalance]");
                    table.CheckConstraint("CK_Wallets_TotalBalance_NonNegative", "[TotalBalance] >= 0");
                    table.CheckConstraint("CK_Wallets_Version_Positive", "[Version] >= 1");
                });

            migrationBuilder.CreateTable(
                name: "FundHolds",
                columns: table => new
                {
                    TradeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuyerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundHolds", x => x.TradeId);
                    table.CheckConstraint("CK_FundHolds_Amount_Positive", "[Amount] > 0");
                    table.CheckConstraint("CK_FundHolds_Status_Valid", "[Status] IN ('Held', 'Settled', 'Released')");
                    table.ForeignKey(
                        name: "FK_FundHolds_Wallets_BuyerId",
                        column: x => x.BuyerId,
                        principalTable: "Wallets",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "TopUpOrders",
                columns: table => new
                {
                    TopUpId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "ZALOPAY"),
                    AppTransId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PaymentUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ProviderTxnId = table.Column<long>(type: "bigint", nullable: true),
                    AmountVnd = table.Column<long>(type: "bigint", nullable: false),
                    GemAmount = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TopUpOrders", x => x.TopUpId);
                    table.CheckConstraint("CK_TopUpOrders_AmountVnd_Positive", "[AmountVnd] > 0");
                    table.CheckConstraint("CK_TopUpOrders_GemAmount_Positive", "[GemAmount] > 0");
                    table.CheckConstraint("CK_TopUpOrders_Provider_ZaloPay", "[Provider] = 'ZALOPAY'");
                    table.CheckConstraint("CK_TopUpOrders_Status_Valid", "[Status] IN ('Pending', 'Paid', 'Failed', 'Expired')");
                    table.ForeignKey(
                        name: "FK_TopUpOrders_Wallets_UserId",
                        column: x => x.UserId,
                        principalTable: "Wallets",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "WalletTransactions",
                columns: table => new
                {
                    EntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TradeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TopUpId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TotalDelta = table.Column<long>(type: "bigint", nullable: false),
                    HoldDelta = table.Column<long>(type: "bigint", nullable: false),
                    FeeGem = table.Column<long>(type: "bigint", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTransactions", x => x.EntryId);
                    table.CheckConstraint("CK_WalletTransactions_FeeGem_TaxBurnOnly", "([Type] = 'TaxBurn' AND [FeeGem] IS NOT NULL AND [FeeGem] >= 0) OR ([Type] <> 'TaxBurn' AND [FeeGem] IS NULL)");
                    table.CheckConstraint("CK_WalletTransactions_Type_Valid", "[Type] IN ('TopUp', 'Hold', 'SettlementDebit', 'SettlementCredit', 'Release', 'TaxBurn')");
                    table.ForeignKey(
                        name: "FK_WalletTransactions_TopUpOrders_TopUpId",
                        column: x => x.TopUpId,
                        principalTable: "TopUpOrders",
                        principalColumn: "TopUpId");
                    table.ForeignKey(
                        name: "FK_WalletTransactions_Wallets_UserId",
                        column: x => x.UserId,
                        principalTable: "Wallets",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FundHolds_BuyerId",
                table: "FundHolds",
                column: "BuyerId");

            migrationBuilder.CreateIndex(
                name: "UX_HttpIdempotencyRecords_ScopeKey",
                table: "HttpIdempotencyRecords",
                columns: new[] { "ActorId", "Operation", "ResourceScope", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TopUpOrders_Status_CreatedAt",
                table: "TopUpOrders",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TopUpOrders_UserId_CreatedAt",
                table: "TopUpOrders",
                columns: new[] { "UserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UX_TopUpOrders_AppTransId",
                table: "TopUpOrders",
                column: "AppTransId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_TopUpOrders_ProviderTxnId",
                table: "TopUpOrders",
                column: "ProviderTxnId",
                unique: true,
                filter: "[ProviderTxnId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_TopUpId",
                table: "WalletTransactions",
                column: "TopUpId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_TradeId",
                table: "WalletTransactions",
                column: "TradeId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_UserId_CreatedAt_EntryId",
                table: "WalletTransactions",
                columns: new[] { "UserId", "CreatedAt", "EntryId" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "UX_WalletTransactions_OperationKey",
                table: "WalletTransactions",
                column: "OperationKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FundHolds");

            migrationBuilder.DropTable(
                name: "HttpIdempotencyRecords");

            migrationBuilder.DropTable(
                name: "WalletTransactions");

            migrationBuilder.DropTable(
                name: "TopUpOrders");

            migrationBuilder.DropTable(
                name: "Wallets");
        }
    }
}
