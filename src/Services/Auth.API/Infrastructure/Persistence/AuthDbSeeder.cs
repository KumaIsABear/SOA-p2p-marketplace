using Auth.API.Application.Interfaces;
using Auth.API.Application.Options;
using Auth.API.Domain.Entities;
using Auth.API.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Auth.API.Infrastructure.Persistence;

public sealed class AuthDbSeeder {
    private static readonly (Guid UserId, string Email, string DisplayName, UserRole Role)[] SeedAccounts = [
        (Guid.Parse("10000000-0000-4000-8000-000000000001"), "gm@marketplace.local", "Game Master", UserRole.GM),
        (Guid.Parse("10000000-0000-4000-8000-000000000002"), "cs@marketplace.local", "Customer Support", UserRole.CS),
        (Guid.Parse("10000000-0000-4000-8000-000000000003"), "seller@marketplace.local", "Demo Seller", UserRole.Player),
        (Guid.Parse("10000000-0000-4000-8000-000000000004"), "buyer@marketplace.local", "Demo Buyer", UserRole.Player),
        (Guid.Parse("10000000-0000-4000-8000-000000000005"), "buyer.full@marketplace.local", "Demo Full-Inventory Buyer", UserRole.Player)
    ];

    private readonly AuthDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly SeedOptions _seedOptions;
    private readonly ILogger<AuthDbSeeder> _logger;

    public AuthDbSeeder(
        AuthDbContext db,
        IPasswordHasher passwordHasher,
        IOptions<SeedOptions> seedOptions,
        ILogger<AuthDbSeeder> logger
    ) {
        _db = db;
        _passwordHasher = passwordHasher;
        _seedOptions = seedOptions.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken) {
        if (!_seedOptions.Enabled || string.IsNullOrWhiteSpace(_seedOptions.DefaultPassword)) {
            return;
        }

        var existingIds = await _db.Users
            .Select(u => u.UserId)
            .ToHashSetAsync(cancellationToken);

        var created = 0;
        foreach (var account in SeedAccounts) {
            if (existingIds.Contains(account.UserId)) {
                continue;
            }

            _db.Users.Add(new User {
                UserId = account.UserId,
                Email = account.Email,
                NormalizedEmail = account.Email.ToUpperInvariant(),
                DisplayName = account.DisplayName,
                PasswordHash = _passwordHasher.Hash(_seedOptions.DefaultPassword),
                Role = account.Role,
                Status = AccountStatus.Active,
                Version = 1
            });
            created++;
        }

        if (created > 0) {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Seeded {Count} AuthDb demo accounts.", created);
        }
    }
}
