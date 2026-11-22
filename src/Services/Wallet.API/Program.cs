using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using Wallet.API.Application.Services;
using Wallet.API.Domain.Errors;
using Wallet.API.Infrastructure.Http;
using Wallet.API.Infrastructure.Options;
using Wallet.API.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<ServiceAuthOptions>(builder.Configuration.GetSection(ServiceAuthOptions.SectionName));
builder.Services.Configure<TopUpPackagesOptions>(builder.Configuration.GetSection(TopUpPackagesOptions.SectionName));

var topUpPackagesOptions = builder.Configuration
    .GetSection(TopUpPackagesOptions.SectionName)
    .Get<TopUpPackagesOptions>() ?? new TopUpPackagesOptions();
topUpPackagesOptions.Validate();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<WalletDbContext>(options =>
    options.UseSqlServer(connectionString));

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey) || Encoding.UTF8.GetByteCount(jwtOptions.SigningKey) < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey must be configured with at least 256 bits (32 bytes).");
}

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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
            IssuerSigningKey = signingKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };

        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                await ProblemDetailsHelper.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status401Unauthorized,
                    WalletErrorCodes.Unauthenticated,
                    "Authentication is required or the access token is invalid.");
            },
            OnForbidden = async context =>
            {
                await ProblemDetailsHelper.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status403Forbidden,
                    WalletErrorCodes.Forbidden,
                    "You do not have permission to access this resource.");
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PlayerOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("Player");
    });
});

builder.Services.AddScoped<IWalletQueryService, WalletQueryService>();

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
                .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid input value." : e.ErrorMessage)
                .Distinct()
                .ToList();

            var problem = ProblemDetailsHelper.Create(
                context.HttpContext,
                StatusCodes.Status400BadRequest,
                WalletErrorCodes.ValidationError,
                "One or more validation errors occurred.",
                errors: errors);

            return new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status400BadRequest,
                ContentTypes = { "application/problem+json" }
            };
        };
    });

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services);

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseHttpMetrics();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapMetrics("/metrics");

app.Run();

public partial class Program { }