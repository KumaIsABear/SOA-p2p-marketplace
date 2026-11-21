namespace Marketplace.Contracts.V1;

public sealed record BuyerFundsHeldEvent(
    Guid CorrelationId,
    Guid OperationId,
    DateTimeOffset OccurredAt,
    Guid TradeId,
    Guid BuyerId,
    long Amount
);

public sealed record BuyerFundsHoldFailedEvent(
    Guid CorrelationId,
    Guid OperationId,
    DateTimeOffset OccurredAt,
    Guid TradeId,
    string FailureCode
);

public sealed record ItemTransferredEvent(
    Guid CorrelationId,
    Guid OperationId,
    DateTimeOffset OccurredAt,
    Guid TradeId,
    Guid ListingId,
    Guid ItemId,
    Guid BuyerId,
    int Quantity
);

public sealed record ItemTransferFailedEvent(
    Guid CorrelationId,
    Guid OperationId,
    DateTimeOffset OccurredAt,
    Guid TradeId,
    string FailureCode
);

public sealed record TradeSettledEvent(
    Guid CorrelationId,
    Guid OperationId,
    DateTimeOffset OccurredAt,
    Guid TradeId,
    long GrossGem,
    long FeeGem,
    long SellerNetGem
);

public sealed record TradeSettlementRejectedEvent(
    Guid CorrelationId,
    Guid OperationId,
    DateTimeOffset OccurredAt,
    Guid TradeId,
    string FailureCode
);

public sealed record BuyerFundsRefundedEvent(
    Guid CorrelationId,
    Guid OperationId,
    DateTimeOffset OccurredAt,
    Guid TradeId,
    long Amount
);

public sealed record ItemTransferReversedEvent(
    Guid CorrelationId,
    Guid OperationId,
    DateTimeOffset OccurredAt,
    Guid TradeId,
    Guid ListingId,
    Guid ItemId
);

public sealed record ItemTransferFinalizedEvent(
    Guid CorrelationId,
    Guid OperationId,
    DateTimeOffset OccurredAt,
    Guid TradeId,
    Guid ListingId,
    Guid ItemId
);

public sealed record ItemUnlockedEvent(
    Guid CorrelationId,
    Guid OperationId,
    DateTimeOffset OccurredAt,
    Guid ListingId,
    Guid ItemId,
    Guid SellerId
);
