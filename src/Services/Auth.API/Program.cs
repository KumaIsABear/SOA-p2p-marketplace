using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Auth.API.Application.DTOs;
using Auth.API.Application.Interfaces;
using Auth.API.Application.Options;
using Auth.API.Application.Services;
using Auth.API.Infrastructure.Middleware;
using Auth.API.Infrastructure.Persistence;
using Auth.API.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Prometheus;

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
    .Validate(o => o.AccessTokenMinutes > 0, "Jwt:AccessTokenMinutes must be positive.")
    .Validate(o => o.RefreshTokenDays > 0, "Jwt:RefreshTokenDays must be positive.")
    .ValidateOnStart();

builder.Services.AddOptions<GoogleAuthOptions>()
    .Bind(builder.Configuration.GetSection(GoogleAuthOptions.SectionName));

builder.Services.AddOptions<SeedOptions>()
    .Bind(builder.Configuration.GetSection(SeedOptions.SectionName));

builder.Services.AddOptions<ServiceAuthOptions>()
    .Bind(builder.Configuration.GetSection(ServiceAuthOptions.SectionName));

builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null))
);

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

builder.Services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
builder.Services.AddSingleton<ITokenService, JwtTokenService>();
builder.Services.AddSingleton<IGoogleTokenValidator, GoogleTokenValidator>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<AuthDbSeeder>();

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
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await db.Database.MigrateAsync();

            var seeder = scope.ServiceProvider.GetRequiredService<AuthDbSeeder>();
            await seeder.SeedAsync(CancellationToken.None);
            return;
        } catch (Exception ex) when (attempt < maxAttempts) {
            logger.LogWarning(ex, "Database initialization attempt {Attempt}/{MaxAttempts} failed. Retrying...", attempt, maxAttempts);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }

    await using var finalScope = services.CreateAsyncScope();
    var finalDb = finalScope.ServiceProvider.GetRequiredService<AuthDbContext>();
    await finalDb.Database.MigrateAsync();
    var finalSeeder = finalScope.ServiceProvider.GetRequiredService<AuthDbSeeder>();
    await finalSeeder.SeedAsync(CancellationToken.None);
}

public partial class Program { }
