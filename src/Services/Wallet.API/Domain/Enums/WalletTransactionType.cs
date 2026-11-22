namespace Wallet.API.Domain.Enums;

public enum WalletTransactionType
{
    TopUp,
    Hold,
    SettlementDebit,
    SettlementCredit,
    Release,
    TaxBurn
}