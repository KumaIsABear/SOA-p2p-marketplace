using Auth.API.Application.Common;
using Auth.API.Application.DTOs;
using Auth.API.Application.Interfaces;
using Auth.API.Domain.Entities;
using Auth.API.Domain.Enums;
using Auth.API.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Auth.API.Application.Services;

public sealed class AuthService : IAuthService {
    private readonly AuthDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IGoogleTokenValidator _googleTokenValidator;

    public AuthService(
        AuthDbContext db,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IGoogleTokenValidator googleTokenValidator
    ) {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _googleTokenValidator = googleTokenValidator;
    }

    public async Task<AuthResult<TokenResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken) {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

        if (user is null || string.IsNullOrEmpty(user.PasswordHash) || !_passwordHasher.Verify(request.Password, user.PasswordHash)) {
            return AuthResult<TokenResponse>.Failure(AuthErrors.InvalidCredentials());
        }

        if (user.Status == AccountStatus.Banned) {
            return AuthResult<TokenResponse>.Failure(AuthErrors.AccountBanned());
        }

        var now = DateTimeOffset.UtcNow;
        var response = await IssueTokensAsync(user, now, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        return AuthResult<TokenResponse>.Success(response);
    }

    public async Task<AuthResult<TokenResponse>> GoogleLoginAsync(GoogleLoginRequest request, CancellationToken cancellationToken) {
        var validation = await _googleTokenValidator.ValidateAsync(request.IdToken.Trim(), cancellationToken);

        if (validation.Status == GoogleValidationStatus.ServiceUnavailable) {
            return AuthResult<TokenResponse>.Failure(AuthErrors.DependencyUnavailable("Google identity verification is currently unavailable."));
        }

        if (validation.Status != GoogleValidationStatus.Valid || validation.Payload is null) {
            return AuthResult<TokenResponse>.Failure(AuthErrors.InvalidGoogleToken());
        }

        var payload = validation.Payload;
        if (!payload.EmailVerified || string.IsNullOrWhiteSpace(payload.Email) || string.IsNullOrWhiteSpace(payload.Subject)) {
            return AuthResult<TokenResponse>.Failure(AuthErrors.InvalidGoogleToken());
        }

        if (!string.IsNullOrWhiteSpace(request.Nonce) && !string.Equals(payload.Nonce, request.Nonce, StringComparison.Ordinal)) {
            return AuthResult<TokenResponse>.Failure(AuthErrors.InvalidGoogleToken());
        }

        var provider = NormalizeGoogleIssuer(payload.Issuer);
        var subject = payload.Subject.Trim();
        var now = DateTimeOffset.UtcNow;

        var existingIdentity = await _db.ExternalIdentities
            .Include(ei => ei.User)
            .FirstOrDefaultAsync(ei => ei.Provider == provider && ei.ProviderSubject == subject, cancellationToken);

        if (existingIdentity is not null) {
            if (existingIdentity.User.Status == AccountStatus.Banned) {
                return AuthResult<TokenResponse>.Failure(AuthErrors.AccountBanned());
            }

            var existingResponse = await IssueTokensAsync(existingIdentity.User, now, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return AuthResult<TokenResponse>.Success(existingResponse);
        }

        var email = payload.Email.Trim().ToLowerInvariant();
        var normalizedEmail = email.ToUpperInvariant();

        var emailExists = await _db.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
        if (emailExists) {
            return AuthResult<TokenResponse>.Failure(AuthErrors.IdentityConflict());
        }

        var displayName = string.IsNullOrWhiteSpace(payload.DisplayName) ? email : payload.DisplayName.Trim();
        if (displayName.Length > 128) {
            displayName = displayName[..128];
        }

        var newUser = new User {
            UserId = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = normalizedEmail,
            DisplayName = displayName,
            PasswordHash = null,
            Role = UserRole.Player,
            Status = AccountStatus.Active,
            Version = 1
        };

        var newIdentity = new ExternalIdentity {
            IdentityId = Guid.NewGuid(),
            UserId = newUser.UserId,
            Provider = provider,
            ProviderSubject = subject
        };

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try {
            _db.Users.Add(newUser);
            _db.ExternalIdentities.Add(newIdentity);
            var response = await IssueTokensAsync(newUser, now, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AuthResult<TokenResponse>.Success(response);
        } catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex)) {
            await transaction.RollbackAsync(cancellationToken);
            return AuthResult<TokenResponse>.Failure(AuthErrors.IdentityConflict());
        }
    }

    public async Task<AuthResult<TokenResponse>> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken) {
        var tokenHash = _tokenService.HashRefreshToken(request.RefreshToken.Trim());
        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var existingToken = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (existingToken is null || existingToken.RevokedAt is not null || existingToken.ExpiresAt <= now) {
            await transaction.RollbackAsync(cancellationToken);
            return AuthResult<TokenResponse>.Failure(AuthErrors.InvalidRefreshToken());
        }

        var rowsUpdated = await _db.RefreshTokens
            .Where(t => t.TokenId == existingToken.TokenId && t.RevokedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, now), cancellationToken);

        if (rowsUpdated == 0) {
            await transaction.RollbackAsync(cancellationToken);
            return AuthResult<TokenResponse>.Failure(AuthErrors.InvalidRefreshToken());
        }

        if (existingToken.User.Status == AccountStatus.Banned) {
            await transaction.CommitAsync(cancellationToken);
            return AuthResult<TokenResponse>.Failure(AuthErrors.AccountBanned());
        }

        var response = await IssueTokensAsync(existingToken.User, now, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return AuthResult<TokenResponse>.Success(response);
    }

    public async Task LogoutAsync(RefreshRequest request, CancellationToken cancellationToken) {
        var tokenHash = _tokenService.HashRefreshToken(request.RefreshToken.Trim());
        var now = DateTimeOffset.UtcNow;

        await _db.RefreshTokens
            .Where(t => t.TokenHash == tokenHash && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, now), cancellationToken);
    }

    private Task<TokenResponse> IssueTokensAsync(User user, DateTimeOffset now, CancellationToken cancellationToken) {
        var accessToken = _tokenService.GenerateAccessToken(user, now);
        var refreshToken = _tokenService.GenerateRefreshToken(now);

        _db.RefreshTokens.Add(new RefreshToken {
            TokenId = Guid.NewGuid(),
            UserId = user.UserId,
            TokenHash = refreshToken.TokenHash,
            ExpiresAt = refreshToken.ExpiresAt,
            RevokedAt = null
        });

        var userDto = new UserDto(
            user.UserId,
            user.Email,
            user.DisplayName,
            user.Role,
            user.Status,
            user.Version
        );

        var response = new TokenResponse(
            accessToken.Token,
            refreshToken.RawToken,
            "Bearer",
            accessToken.ExpiresInSeconds,
            userDto
        );

        return Task.FromResult(response);
    }

    private static string NormalizeGoogleIssuer(string issuer) {
        var trimmed = issuer.Trim();
        return string.Equals(trimmed, "accounts.google.com", StringComparison.OrdinalIgnoreCase)
            ? "https://accounts.google.com"
            : trimmed;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) {
        return ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627);
    }
}
