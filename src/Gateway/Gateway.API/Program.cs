using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Gateway.API.Application.Interfaces;
using Gateway.API.Application.Options;
using Gateway.API.Infrastructure.Clients;
using Gateway.API.Infrastructure.Http;
using Gateway.API.Infrastructure.Middleware;
using Gateway.API.Infrastructure.Routing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer), "Jwt:Issuer is required.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Audience), "Jwt:Audience is required.")
    .Validate(o => Encoding.UTF8.GetByteCount(o.SigningKey ?? string.Empty) >= 32, "Jwt:SigningKey must have at least 256 bits (32 bytes).")
    .ValidateOnStart();

builder.Services.AddOptions<ServicesOptions>()
    .Bind(builder.Configuration.GetSection(ServicesOptions.SectionName))
    .Validate(o => Uri.TryCreate(o.Auth.BaseUrl, UriKind.Absolute, out _), "Services:Auth:BaseUrl must be a valid URI.")
    .Validate(o => Uri.TryCreate(o.Wallet.BaseUrl, UriKind.Absolute, out _), "Services:Wallet:BaseUrl must be a valid URI.")
    .Validate(o => Uri.TryCreate(o.Inventory.BaseUrl, UriKind.Absolute, out _), "Services:Inventory:BaseUrl must be a valid URI.")
    .Validate(o => Uri.TryCreate(o.Marketplace.BaseUrl, UriKind.Absolute, out _), "Services:Marketplace:BaseUrl must be a valid URI.")
    .Validate(o => Uri.TryCreate(o.Admin.BaseUrl, UriKind.Absolute, out _), "Services:Admin:BaseUrl must be a valid URI.")
    .ValidateOnStart();

builder.Services.AddOptions<ServiceAuthOptions>()
    .Bind(builder.Configuration.GetSection(ServiceAuthOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.OutgoingKey), "ServiceAuth:OutgoingKey is required.")
    .ValidateOnStart();

builder.Services.AddOptions<CorsOptions>()
    .Bind(builder.Configuration.GetSection(CorsOptions.SectionName));

builder.Services.AddOptions<RateLimitingOptions>()
    .Bind(builder.Configuration.GetSection(RateLimitingOptions.SectionName))
    .Validate(o => o.Auth.PermitLimit > 0 && o.Auth.WindowSeconds > 0, "RateLimiting:Auth must have positive limits.")
    .Validate(o => o.TopUp.PermitLimit > 0 && o.TopUp.WindowSeconds > 0, "RateLimiting:TopUp must have positive limits.")
    .Validate(o => o.Purchase.PermitLimit > 0 && o.Purchase.WindowSeconds > 0, "RateLimiting:Purchase must have positive limits.")
    .ValidateOnStart();

builder.Services.AddOptions<GatewayOptions>()
    .Bind(builder.Configuration.GetSection(GatewayOptions.SectionName))
    .Validate(o => o.AuthStatusTimeoutSeconds > 0, "Gateway:AuthStatusTimeoutSeconds must be positive.")
    .ValidateOnStart();

builder.Services.Configure<ForwardedHeadersOptions>(options => {
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
});

var servicesOptions = builder.Configuration.GetSection(ServicesOptions.SectionName).Get<ServicesOptions>() ?? new ServicesOptions();
var corsOptions = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();
var rateLimitingOptions = builder.Configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new RateLimitingOptions();

builder.Services.AddHttpClient<IAuthStatusClient, AuthStatusClient>((sp, client) => {
    var svc = sp.GetRequiredService<IOptions<ServicesOptions>>().Value;
    var gw = sp.GetRequiredService<IOptions<GatewayOptions>>().Value;
    client.BaseAddress = new Uri(svc.Auth.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(gw.AuthStatusTimeoutSeconds);
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwtOptionsAccessor) => {
        var jwtOptions = jwtOptionsAccessor.Value;
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));

        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };

        options.Events = new JwtBearerEvents {
            OnChallenge = async context => {
                context.HandleResponse();
                await ProblemResponseWriter.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    "UNAUTHENTICATED",
                    "Authentication is required or the access token is invalid."
                );
            },
            OnForbidden = async context => {
                await ProblemResponseWriter.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status403Forbidden,
                    "Forbidden",
                    "FORBIDDEN",
                    "You do not have permission to access this resource."
                );
            }
        };
    });

builder.Services.AddAuthorization(options => {
    options.AddPolicy(GatewayPolicies.AnyAuthenticated, policy => policy.RequireRole("Player", "GM", "CS"));
    options.AddPolicy(GatewayPolicies.PlayerOnly, policy => policy.RequireRole("Player"));
    options.AddPolicy(GatewayPolicies.GmOnly, policy => policy.RequireRole("GM"));
    options.AddPolicy(GatewayPolicies.CsOnly, policy => policy.RequireRole("CS"));
});

builder.Services.AddCors(options => {
    options.AddPolicy(GatewayPolicies.CorsPolicy, policy => {
        var origins = corsOptions.AllowedOrigins
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (origins.Length > 0) {
            policy.WithOrigins(origins)
                .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
                .WithHeaders("Authorization", "Content-Type", "Accept", "Idempotency-Key", "X-Correlation-ID")
                .WithExposedHeaders("X-Correlation-ID", "Location", "Retry-After");
        }
    });
});

builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(GatewayPolicies.AuthRateLimit, httpContext => {
        var partitionKey = $"auth:{GetClientIp(httpContext)}";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions {
            PermitLimit = rateLimitingOptions.Auth.PermitLimit,
            Window = TimeSpan.FromSeconds(rateLimitingOptions.Auth.WindowSeconds),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });

    options.AddPolicy(GatewayPolicies.TopUpRateLimit, httpContext => {
        var actor = httpContext.User.FindFirstValue("sub") ?? GetClientIp(httpContext);
        var partitionKey = $"topup:{actor}";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions {
            PermitLimit = rateLimitingOptions.TopUp.PermitLimit,
            Window = TimeSpan.FromSeconds(rateLimitingOptions.TopUp.WindowSeconds),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });

    options.AddPolicy(GatewayPolicies.PurchaseRateLimit, httpContext => {
        var actor = httpContext.User.FindFirstValue("sub") ?? GetClientIp(httpContext);
        var partitionKey = $"purchase:{actor}";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions {
            PermitLimit = rateLimitingOptions.Purchase.PermitLimit,
            Window = TimeSpan.FromSeconds(rateLimitingOptions.Purchase.WindowSeconds),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });

    options.OnRejected = async (context, cancellationToken) => {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)) {
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        await ProblemResponseWriter.WriteAsync(
            context.HttpContext,
            StatusCodes.Status429TooManyRequests,
            "Too Many Requests",
            "RATE_LIMITED",
            "Rate limit exceeded. Please retry later."
        );
    };
});

builder.Services.AddReverseProxy()
    .LoadFromMemory(GatewayRouteRegistry.BuildRoutes(), GatewayRouteRegistry.BuildClusters(servicesOptions))
    .AddTransforms(builderContext => {
        builderContext.AddRequestTransform(transformContext => {
            transformContext.ProxyRequest.Headers.Remove(CorrelationIdMiddleware.HeaderName);
            transformContext.ProxyRequest.Headers.TryAddWithoutValidation(
                CorrelationIdMiddleware.HeaderName,
                transformContext.HttpContext.TraceIdentifier
            );
            return ValueTask.CompletedTask;
        });
    });

builder.Services.AddControllers()
    .AddJsonOptions(options => {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<TechnicalEndpointGuardMiddleware>();
app.UseForwardedHeaders();
app.UseHttpMetrics();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseCors(GatewayPolicies.CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapMetrics("/metrics");
app.MapReverseProxy(proxyPipeline => {
    proxyPipeline.UseMiddleware<UpstreamErrorProblemMiddleware>();
    proxyPipeline.UseMiddleware<AccountStatusGuardMiddleware>();
});

app.Run();

static string GetClientIp(HttpContext context) {
    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
builder.Services.AddOptions<GoogleOptions>()
    .Bind(builder.Configuration.GetSection(GoogleOptions.SectionName));

app.MapGet("/demo-config", (IOptions<GoogleOptions> googleOptions) =>
    Results.Ok(new {
        googleClientId = googleOptions.Value.ClientId ?? string.Empty
    })
)
    .AllowAnonymous()
    .RequireCors(GatewayPolicies.CorsPolicy);

public partial class Program { }
