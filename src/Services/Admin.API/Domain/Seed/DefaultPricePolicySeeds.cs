namespace Admin.API.Domain.Seed;

public static class DefaultPricePolicySeeds {
    public static readonly (Guid TemplateId, long BasePrice)[] TradableTemplates = [
        (Guid.Parse("11111111-1111-4111-8111-000000000001"), 1000L),
        (Guid.Parse("11111111-1111-4111-8111-000000000002"), 600L),
        (Guid.Parse("11111111-1111-4111-8111-000000000003"), 100L),
        (Guid.Parse("11111111-1111-4111-8111-000000000004"), 50L)
    ];
}
