using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Marketplace.API.Application.Common;
using Marketplace.API.Application.Interfaces;
using Marketplace.API.Application.Options;
using Marketplace.API.Application.Services;
using Marketplace.API.Infrastructure.Caching;
using Marketplace.API.Infrastructure.Clients;
using Marketplace.API.Infrastructure.Http;
using Marketplace.API.Infrastructure.Locking;
using Marketplace.API.Infrastructure.Messaging;
using Marketplace.API.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using StackExchange.Redis;
using Marketplace.API.Application.DTOs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<EconomyOptions>(builder.Configuration.GetSection(EconomyOptions.SectionName));
builder.Services.Configure<InternalServicesOptions>(builder.Configuration.GetSection(InternalServicesOptions.SectionName));

builder.Services.AddHttpContextAccessor();

builder.Services.AddDbContext<MarketplaceDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var redisConn = builder.Configuration.GetConnectionString("Redis") ?? "redis:6379";
    var config = ConfigurationOptions.Parse(redisConn);
    config.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(config);
});

builder.Services.AddTransient<InternalServiceHeaderHandler>();

builder.Services.AddHttpClient<IAuthStatusClient, AuthStatusClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<InternalServicesOptions>>().Value;
    client.BaseAddress = new Uri(opts.AuthBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
}).AddHttpMessageHandler<InternalServiceHeaderHandler>();

builder.Services.AddHttpClient<IInventoryClient, InventoryClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<InternalServicesOptions>>().Value;
    client.BaseAddress = new Uri(opts.InventoryBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
}).AddHttpMessageHandler<InternalServiceHeaderHandler>();

builder.Services.AddHttpClient<IPricePolicyProvider, RedisAndAdminPricePolicyProvider>((sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<InternalServicesOptions>>().Value;
    client.BaseAddress = new Uri(opts.AdminBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
}).AddHttpMessageHandler<InternalServiceHeaderHandler>();

builder.Services.AddScoped<IDistributedListingLock, RedisDistributedListingLock>();
builder.Services.AddScoped<IListingCommandService, ListingCommandService>();

builder.Services.AddMarketplaceMessaging(builder.Configuration);

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async ctx =>
            {
                ctx.HandleResponse();
                await GlobalExceptionMiddleware.WriteProblemAsync(
                    ctx.HttpContext,
                    StatusCodes.Status401Unauthorized,
                    MarketplaceErrorCodes.Unauthenticated,
                    "Authentication is required."
                );
            },
            OnForbidden = async ctx =>
            {
                await GlobalExceptionMiddleware.WriteProblemAsync(
                    ctx.HttpContext,
                    StatusCodes.Status403Forbidden,
                    MarketplaceErrorCodes.Forbidden,
                    "You do not have permission to perform this action."
                );
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid input." : e.ErrorMessage)
                .ToArray();

            var traceId = context.HttpContext.Items[CorrelationIdMiddleware.ItemKey]?.ToString()
                ?? context.HttpContext.TraceIdentifier;

            var problem = new ApiProblemDetails(
                Type: "about:blank",
                Title: "Bad Request",
                Status: StatusCodes.Status400BadRequest,
                Detail: "Request validation failed.",
                Instance: context.HttpContext.Request.Path.Value ?? "/",
                Code: MarketplaceErrorCodes.ValidationError,
                TraceId: traceId,
                Errors: errors
            );

            return new BadRequestObjectResult(problem)
            {
                ContentTypes = { "application/problem+json" }
            };
        };
    });

var app = builder.Build();

if (app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.AutoMigrate)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
    db.Database.Migrate();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseHttpMetrics();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapMetrics("/metrics");

app.Run();

public partial class Program { }
