using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Admin.API.Application.Common;
using Admin.API.Application.DTOs;
using Admin.API.Application.Interfaces;
using Admin.API.Application.Options;
using Admin.API.Application.Services;
using Admin.API.Infrastructure.Background;
using Admin.API.Infrastructure.Caching;
using Admin.API.Infrastructure.Clients;
using Admin.API.Infrastructure.Messaging;
using Admin.API.Infrastructure.Middleware;
using Admin.API.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString)) {
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");
}

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer), "Jwt:Issuer is required.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Audience), "Jwt:Audience is required.")
    .Validate(o => Encoding.UTF8.GetByteCount(o.SigningKey ?? string.Empty) >= 32, "Jwt:SigningKey must have at least 256 bits (32 bytes).")
    .ValidateOnStart();

builder.Services.AddOptions<RedisOptions>()
    .Bind(builder.Configuration.GetSection(RedisOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "Redis:ConnectionString is required.")
    .ValidateOnStart();

builder.Services.AddOptions<EconomyOptions>()
    .Bind(builder.Configuration.GetSection(EconomyOptions.SectionName))
    .Validate(
        o => o.DefaultBandBps >= PriceBoundsCalculator.MinBandBasisPoints
            && o.DefaultBandBps <= PriceBoundsCalculator.MaxBandBasisPoints,
        "Economy:DefaultBandBps must be between 0 and 9999."
    )
    .ValidateOnStart();

builder.Services.AddOptions<AnalyticsOptions>()
    .Bind(builder.Configuration.GetSection(AnalyticsOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.TimeZoneId), "Analytics:TimeZoneId is required.")
    .ValidateOnStart();

builder.Services.AddOptions<ServicesOptions>()
    .Bind(builder.Configuration.GetSection(ServicesOptions.SectionName))
    .Validate(o => Uri.TryCreate(o.Auth.BaseUrl, UriKind.Absolute, out _), "Services:Auth:BaseUrl must be a valid URI.")
    .Validate(o => Uri.TryCreate(o.Wallet.BaseUrl, UriKind.Absolute, out _), "Services:Wallet:BaseUrl must be a valid URI.")
    .Validate(o => Uri.TryCreate(o.Inventory.BaseUrl, UriKind.Absolute, out _), "Services:Inventory:BaseUrl must be a valid URI.")
    .Validate(o => Uri.TryCreate(o.Marketplace.BaseUrl, UriKind.Absolute, out _), "Services:Marketplace:BaseUrl must be a valid URI.")
    .Validate(o => o.TimeoutSeconds > 0, "Services:TimeoutSeconds must be positive.")
    .ValidateOnStart();

builder.Services.AddOptions<SeedOptions>()
    .Bind(builder.Configuration.GetSection(SeedOptions.SectionName));

builder.Services.AddOptions<ServiceAuthOptions>()
    .Bind(builder.Configuration.GetSection(ServiceAuthOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.OutgoingKey), "ServiceAuth:OutgoingKey is required.")
    .ValidateOnStart();

builder.Services.AddDbContext<AdminDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null))
);

builder.Services.AddSingleton<IConnectionMultiplexer>(sp => {
    var redisOptions = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
    var config = ConfigurationOptions.Parse(redisOptions.ConnectionString);
    config.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(config);
});

builder.Services.AddSingleton<IPricePolicyCache, RedisPricePolicyCache>();
builder.Services.AddSingleton<IAnalyticsCache, RedisAnalyticsCache>();
builder.Services.AddScoped<IPricePolicyService, PricePolicyService>();
builder.Services.AddScoped<IMarketAnalyticsService, MarketAnalyticsService>();
builder.Services.AddScoped<ISupportService, SupportService>();
builder.Services.AddScoped<AdminDbSeeder>();

builder.Services.AddHttpClient<IAuthInternalClient, AuthInternalClient>((sp, client) => {
    var svc = sp.GetRequiredService<IOptions<ServicesOptions>>().Value;
    client.BaseAddress = new Uri(svc.Auth.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(svc.TimeoutSeconds);
});

builder.Services.AddHttpClient<IWalletInternalClient, WalletInternalClient>((sp, client) => {
    var svc = sp.GetRequiredService<IOptions<ServicesOptions>>().Value;
    client.BaseAddress = new Uri(svc.Wallet.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(svc.TimeoutSeconds);
});

builder.Services.AddHttpClient<IInventoryInternalClient, InventoryInternalClient>((sp, client) => {
    var svc = sp.GetRequiredService<IOptions<ServicesOptions>>().Value;
    client.BaseAddress = new Uri(svc.Inventory.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(svc.TimeoutSeconds);
});

builder.Services.AddHttpClient<IMarketplaceInternalClient, MarketplaceInternalClient>((sp, client) => {
    var svc = sp.GetRequiredService<IOptions<ServicesOptions>>().Value;
    client.BaseAddress = new Uri(svc.Marketplace.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(svc.TimeoutSeconds);
});

builder.Services.AddAdminMessaging(builder.Configuration);
builder.Services.AddHostedService<MarketDailySummaryWorker>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

var problemJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

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
                await WriteAuthProblemAsync(
                    context.HttpContext,
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    "UNAUTHENTICATED",
                    "Authentication is required or the access token is invalid.",
                    problemJsonOptions
                );
            },
            OnForbidden = async context => {
                await WriteAuthProblemAsync(
                    context.HttpContext,
                    StatusCodes.Status403Forbidden,
                    "Forbidden",
                    "FORBIDDEN",
                    "You do not have permission to access this resource.",
                    problemJsonOptions
                );
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddControllers()
    .AddJsonOptions(options => {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    })
    .ConfigureApiBehaviorOptions(options => {
        options.InvalidModelStateResponseFactory = context => {
            var errors = context.ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid input." : e.ErrorMessage)
                .Distinct()
                .ToArray();

            var problem = new ApiProblemDetails(
                "about:blank",
                "Bad Request",
                StatusCodes.Status400BadRequest,
                "One or more validation errors occurred.",
                context.HttpContext.Request.Path.Value ?? "/",
                "VALIDATION_ERROR",
                context.HttpContext.TraceIdentifier,
                errors
            );

            return new ObjectResult(problem) {
                StatusCode = StatusCodes.Status400BadRequest,
                ContentTypes = { "application/problem+json" }
            };
        };
    });

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseHttpMetrics();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapMetrics("/metrics");

if (builder.Configuration.GetValue("Database:AutoMigrate", true)) {
    await InitializeDatabaseAsync(app.Services, app.Logger);
}

app.Run();

static async Task WriteAuthProblemAsync(
    HttpContext context,
    int status,
    string title,
    string code,
    string detail,
    JsonSerializerOptions jsonOptions
) {
    if (context.Response.HasStarted) {
        return;
    }

    context.Response.StatusCode = status;
    context.Response.ContentType = "application/problem+json";

    var problem = new ApiProblemDetails(
        "about:blank",
        title,
        status,
        detail,
        context.Request.Path.Value ?? "/",
        code,
        context.TraceIdentifier
    );

    await context.Response.WriteAsync(JsonSerializer.Serialize(problem, jsonOptions));
}

static async Task InitializeDatabaseAsync(IServiceProvider services, ILogger logger) {
    const int maxAttempts = 5;
    for (var attempt = 1; attempt <= maxAttempts; attempt++) {
        try {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            await db.Database.MigrateAsync();

            var seeder = scope.ServiceProvider.GetRequiredService<AdminDbSeeder>();
            await seeder.SeedAsync(CancellationToken.None);

            var pricePolicyService = scope.ServiceProvider.GetRequiredService<IPricePolicyService>();
            await pricePolicyService.WarmCacheAsync(CancellationToken.None);
            return;
        } catch (Exception ex) when (attempt < maxAttempts) {
            logger.LogWarning(ex, "Admin database initialization attempt {Attempt}/{MaxAttempts} failed. Retrying...", attempt, maxAttempts);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }

    await using var finalScope = services.CreateAsyncScope();
    var finalDb = finalScope.ServiceProvider.GetRequiredService<AdminDbContext>();
    await finalDb.Database.MigrateAsync();
    var finalSeeder = finalScope.ServiceProvider.GetRequiredService<AdminDbSeeder>();
    await finalSeeder.SeedAsync(CancellationToken.None);
    var finalPricePolicyService = finalScope.ServiceProvider.GetRequiredService<IPricePolicyService>();
    await finalPricePolicyService.WarmCacheAsync(CancellationToken.None);
}

public partial class Program { }
