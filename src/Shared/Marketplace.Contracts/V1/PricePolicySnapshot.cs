namespace Marketplace.Contracts.V1;

public sealed record PricePolicySnapshot(
    Guid TemplateId,
    long BasePrice,
    long ReferencePrice,
    int BandBasisPoints,
    long MinUnitPrice,
    long MaxUnitPrice,
    long Version,
    DateTimeOffset AsOf
);
