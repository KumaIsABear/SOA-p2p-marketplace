namespace Marketplace.Contracts.V1;

public sealed record ListingCreatedEvent(
    Guid CorrelationId,
    Guid OperationId,
    DateTimeOffset OccurredAt,
    ListingSnapshot Listing
);

public sealed record ListingProcessingEvent(
    Guid CorrelationId,
    Guid OperationId,
    DateTimeOffset OccurredAt,
    ListingSnapshot Listing
);

public sealed record ListingCancelledEvent(
    Guid CorrelationId,
    Guid OperationId,
    DateTimeOffset OccurredAt,
    ListingSnapshot Listing
);

public sealed record ListingReactivatedEvent(
    Guid CorrelationId,
    Guid OperationId,
    DateTimeOffset OccurredAt,
    ListingSnapshot Listing
);

public sealed record TradeCompletedEvent(
    Guid CorrelationId,
    Guid OperationId,
    DateTimeOffset OccurredAt,
    ListingSnapshot Listing,
    Guid TradeId,
    Guid BuyerId,
    Guid SellerId,
    Guid TemplateId,
    int Quantity,
    long UnitPrice,
    long GrossGem,
    long FeeGem,
    long SellerNetGem,
    DateTimeOffset CompletedAt
);

public sealed record TradeFailedEvent(
    Guid CorrelationId,
    Guid OperationId,
    DateTimeOffset OccurredAt,
    Guid TradeId,
    Guid ListingId,
    string FailureCode,
    DateTimeOffset FailedAt
);
