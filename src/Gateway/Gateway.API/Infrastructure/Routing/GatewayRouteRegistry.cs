using Gateway.API.Application.Options;
using Yarp.ReverseProxy.Configuration;

namespace Gateway.API.Infrastructure.Routing;

public static class GatewayRouteRegistry {
    private const string AuthCluster = "auth-cluster";
    private const string WalletCluster = "wallet-cluster";
    private const string InventoryCluster = "inventory-cluster";
    private const string MarketplaceCluster = "marketplace-cluster";
    private const string AdminCluster = "admin-cluster";

    private static readonly IReadOnlyList<IReadOnlyDictionary<string, string>> StripSpoofedHeadersTransforms = [
        new Dictionary<string, string> { ["RequestHeaderRemove"] = "X-Service-Key" },
        new Dictionary<string, string> { ["RequestHeaderRemove"] = "X-User-Id" },
        new Dictionary<string, string> { ["RequestHeaderRemove"] = "X-User-Role" },
        new Dictionary<string, string> { ["RequestHeaderRemove"] = "X-Account-Status" },
        new Dictionary<string, string> { ["RequestHeaderRemove"] = "X-Forwarded-User" }
    ];

    public static IReadOnlyList<ClusterConfig> BuildClusters(ServicesOptions services) {
        return [
            CreateCluster(AuthCluster, services.Auth.BaseUrl),
            CreateCluster(WalletCluster, services.Wallet.BaseUrl),
            CreateCluster(InventoryCluster, services.Inventory.BaseUrl),
            CreateCluster(MarketplaceCluster, services.Marketplace.BaseUrl),
            CreateCluster(AdminCluster, services.Admin.BaseUrl)
        ];
    }

    public static IReadOnlyList<RouteConfig> BuildRoutes() {
        return [
            CreateAnonymousRoute("auth-01-login", AuthCluster, "POST", "/api/v1/auth/login", GatewayPolicies.AuthRateLimit),
            CreateAnonymousRoute("auth-02-google", AuthCluster, "POST", "/api/v1/auth/google", GatewayPolicies.AuthRateLimit),
            CreateAnonymousRoute("auth-03-refresh", AuthCluster, "POST", "/api/v1/auth/refresh", GatewayPolicies.AuthRateLimit),
            CreateAnonymousRoute("auth-04-logout", AuthCluster, "POST", "/api/v1/auth/logout"),
            CreateProtectedRoute("auth-05-me", AuthCluster, "GET", "/api/v1/auth/me", GatewayPolicies.AnyAuthenticated, "Player,GM,CS"),

            CreateProtectedRoute("wal-01-balance", WalletCluster, "GET", "/api/v1/wallet/balance", GatewayPolicies.PlayerOnly, "Player"),
            CreateProtectedRoute("wal-02-transactions", WalletCluster, "GET", "/api/v1/wallet/transactions", GatewayPolicies.PlayerOnly, "Player"),
            CreateProtectedRoute("wal-03-packages", WalletCluster, "GET", "/api/v1/wallet/top-up-packages", GatewayPolicies.PlayerOnly, "Player"),
            CreateProtectedRoute("wal-04-create-topup", WalletCluster, "POST", "/api/v1/wallet/top-ups", GatewayPolicies.PlayerOnly, "Player", rateLimiterPolicy: GatewayPolicies.TopUpRateLimit),
            CreateProtectedRoute("wal-05-get-topup", WalletCluster, "GET", "/api/v1/wallet/top-ups/{topUpId}", GatewayPolicies.PlayerOnly, "Player"),
            CreateAnonymousRoute("wal-06-zalopay-callback", WalletCluster, "POST", "/api/v1/wallet/zalopay/callback"),

            CreateProtectedRoute("inv-01-items", InventoryCluster, "GET", "/api/v1/inventory/items", GatewayPolicies.PlayerOnly, "Player"),

            CreateProtectedRoute("mkt-01-create-listing", MarketplaceCluster, "POST", "/api/v1/marketplace/listings", GatewayPolicies.PlayerOnly, "Player", requireCanTrade: true),
            CreateProtectedRoute("mkt-02-cancel-listing", MarketplaceCluster, "DELETE", "/api/v1/marketplace/listings/{listingId}", GatewayPolicies.PlayerOnly, "Player"),
            CreateProtectedRoute("mkt-03-purchase", MarketplaceCluster, "POST", "/api/v1/marketplace/listings/{listingId}/purchase", GatewayPolicies.PlayerOnly, "Player", requireCanTrade: true, rateLimiterPolicy: GatewayPolicies.PurchaseRateLimit),
            CreateProtectedRoute("mkt-04-list-catalog", MarketplaceCluster, "GET", "/api/v1/marketplace/listings", GatewayPolicies.PlayerOnly, "Player"),
            CreateProtectedRoute("mkt-05-get-listing", MarketplaceCluster, "GET", "/api/v1/marketplace/listings/{listingId}", GatewayPolicies.PlayerOnly, "Player"),
            CreateProtectedRoute("mkt-06-my-listings", MarketplaceCluster, "GET", "/api/v1/marketplace/my-listings", GatewayPolicies.PlayerOnly, "Player"),
            CreateProtectedRoute("mkt-07-history", MarketplaceCluster, "GET", "/api/v1/marketplace/history", GatewayPolicies.PlayerOnly, "Player"),
            CreateProtectedRoute("mkt-08-get-trade", MarketplaceCluster, "GET", "/api/v1/marketplace/trades/{tradeId}", GatewayPolicies.PlayerOnly, "Player"),

            CreateProtectedRoute("adm-01-analytics", AdminCluster, "GET", "/api/v1/admin/analytics", GatewayPolicies.GmOnly, "GM"),
            CreateProtectedRoute("adm-02-analytics-history", AdminCluster, "GET", "/api/v1/admin/analytics/history", GatewayPolicies.GmOnly, "GM"),
            CreateProtectedRoute("adm-03-price-policies-list", AdminCluster, "GET", "/api/v1/admin/price-policies", GatewayPolicies.GmOnly, "GM"),
            CreateProtectedRoute("adm-04-price-policies-upsert", AdminCluster, "PUT", "/api/v1/admin/price-policies/{templateId}", GatewayPolicies.GmOnly, "GM"),
            CreateProtectedRoute("adm-05-trade-trace", AdminCluster, "GET", "/api/v1/admin/trades/{tradeId}/trace", GatewayPolicies.CsOnly, "CS"),
            CreateProtectedRoute("adm-06-trades-search", AdminCluster, "GET", "/api/v1/admin/trades", GatewayPolicies.CsOnly, "CS"),
            CreateProtectedRoute("adm-07-moderate-user", AdminCluster, "PATCH", "/api/v1/admin/users/{userId}/status", GatewayPolicies.CsOnly, "CS")
        ];
    }

    private static ClusterConfig CreateCluster(string clusterId, string address) {
        return new ClusterConfig {
            ClusterId = clusterId,
            Destinations = new Dictionary<string, DestinationConfig> {
                ["primary"] = new() { Address = address }
            }
        };
    }

    private static RouteConfig CreateAnonymousRoute(
        string routeId,
        string clusterId,
        string method,
        string path,
        string? rateLimiterPolicy = null
    ) {
        return new RouteConfig {
            RouteId = routeId,
            ClusterId = clusterId,
            CorsPolicy = GatewayPolicies.CorsPolicy,
            RateLimiterPolicy = rateLimiterPolicy,
            Match = new RouteMatch {
                Path = path,
                Methods = [method]
            },
            Metadata = new Dictionary<string, string> {
                [GatewayRouteMetadata.RequireAccountStatus] = "false",
                [GatewayRouteMetadata.RequireCanTrade] = "false"
            },
            Transforms = StripSpoofedHeadersTransforms
        };
    }

    private static RouteConfig CreateProtectedRoute(
        string routeId,
        string clusterId,
        string method,
        string path,
        string authorizationPolicy,
        string requiredRolesCsv,
        bool requireCanTrade = false,
        string? rateLimiterPolicy = null
    ) {
        return new RouteConfig {
            RouteId = routeId,
            ClusterId = clusterId,
            AuthorizationPolicy = authorizationPolicy,
            CorsPolicy = GatewayPolicies.CorsPolicy,
            RateLimiterPolicy = rateLimiterPolicy,
            Match = new RouteMatch {
                Path = path,
                Methods = [method]
            },
            Metadata = new Dictionary<string, string> {
                [GatewayRouteMetadata.RequireAccountStatus] = "true",
                [GatewayRouteMetadata.RequireCanTrade] = requireCanTrade ? "true" : "false",
                [GatewayRouteMetadata.RequiredRoles] = requiredRolesCsv
            },
            Transforms = StripSpoofedHeadersTransforms
        };
    }
}
