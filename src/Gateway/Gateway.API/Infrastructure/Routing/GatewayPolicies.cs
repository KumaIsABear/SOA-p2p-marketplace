namespace Gateway.API.Infrastructure.Routing;

public static class GatewayPolicies {
    public const string AnyAuthenticated = "AnyAuthenticated";
    public const string PlayerOnly = "PlayerOnly";
    public const string GmOnly = "GmOnly";
    public const string CsOnly = "CsOnly";

    public const string AuthRateLimit = "auth-rate";
    public const string TopUpRateLimit = "topup-rate";
    public const string PurchaseRateLimit = "purchase-rate";

    public const string CorsPolicy = "GatewayCors";
}

public static class GatewayRouteMetadata {
    public const string RequireAccountStatus = "RequireAccountStatus";
    public const string RequireCanTrade = "RequireCanTrade";
    public const string RequiredRoles = "RequiredRoles";
}
