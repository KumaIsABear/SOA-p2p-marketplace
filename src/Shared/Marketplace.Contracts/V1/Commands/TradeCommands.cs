namespace Marketplace.Contracts.V1;

public sealed record HoldBuyerFundsCommand(
    Guid CorrelationId,
    Guid OperationId,
    Guid TradeId,
    Guid BuyerId,
    long Amount
);

public sealed record TransferItemCommand(
    Guid CorrelationId,
    Guid OperationId,
    Guid TradeId,
    Guid ListingId,
    Guid ItemId,
    Guid TemplateId,
    Guid SellerId,
    Guid BuyerId,
    int Quantity
);

public sealed record SettleTradeCommand(
    Guid CorrelationId,
    Guid OperationId,
    Guid TradeId,
    Guid BuyerId,
    Guid SellerId,
    long GrossGem,
    long FeeGem,
    long SellerNetGem
);

public sealed record RefundBuyerFundsCommand(
    Guid CorrelationId,
    Guid OperationId,
    Guid TradeId,
    Guid BuyerId,
    long Amount
);

public sealed record ReverseItemTransferCommand(
    Guid CorrelationId,
    Guid OperationId,
    Guid TradeId,
    Guid ListingId,
    Guid ItemId,
    Guid SellerId,
    Guid BuyerId
);

public sealed record FinalizeItemTransferCommand(
    Guid CorrelationId,
    Guid OperationId,
    Guid TradeId,
    Guid ListingId,
    Guid ItemId,
    Guid BuyerId
);

public sealed record UnlockItemCommand(
    Guid CorrelationId,
    Guid OperationId,
    Guid ListingId,
    Guid ItemId,
    Guid SellerId
);
