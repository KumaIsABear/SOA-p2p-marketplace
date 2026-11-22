using System.Data;
using System.Security.Cryptography;
using System.Text;
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
    private const int MaxConcurrencyRetries = 3;

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
        var response = IssueTokens(user, now);
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
        var email = payload.Email.Trim().ToLowerInvariant();
        var normalizedEmail = email.ToUpperInvariant();
        var displayName = string.IsNullOrWhiteSpace(payload.DisplayName) ? email : payload.DisplayName.Trim();
        if (displayName.Length > 128) {
            displayName = displayName[..128];
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () => {
            _db.ChangeTracker.Clear();
            var now = DateTimeOffset.UtcNow;

            var existingIdentity = await _db.ExternalIdentities
                .Include(ei => ei.User)
                .FirstOrDefaultAsync(ei => ei.Provider == provider && ei.ProviderSubject == subject, cancellationToken);

            if (existingIdentity is not null) {
                if (existingIdentity.User.Status == AccountStatus.Banned) {
                    return AuthResult<TokenResponse>.Failure(AuthErrors.AccountBanned());
                }

                var existingResponse = IssueTokens(existingIdentity.User, now);
                await _db.SaveChangesAsync(cancellationToken);
                return AuthResult<TokenResponse>.Success(existingResponse);
            }

            var emailExists = await _db.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
            if (emailExists) {
                return AuthResult<TokenResponse>.Failure(AuthErrors.IdentityConflict());
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

            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            try {
                _db.Users.Add(newUser);
                _db.ExternalIdentities.Add(newIdentity);
                var response = IssueTokens(newUser, now);
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return AuthResult<TokenResponse>.Success(response);
            } catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex)) {
                await transaction.RollbackAsync(cancellationToken);
                return AuthResult<TokenResponse>.Failure(AuthErrors.IdentityConflict());
            }
        });
    }

    public async Task<AuthResult<TokenResponse>> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken) {
        var tokenHash = _tokenService.HashRefreshToken(request.RefreshToken.Trim());
        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () => {
            _db.ChangeTracker.Clear();
            var now = DateTimeOffset.UtcNow;

            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

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

            var response = IssueTokens(existingToken.User, now);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return AuthResult<TokenResponse>.Success(response);
        });
    }

    public async Task LogoutAsync(RefreshRequest request, CancellationToken cancellationToken) {
        var tokenHash = _tokenService.HashRefreshToken(request.RefreshToken.Trim());
        var now = DateTimeOffset.UtcNow;

        await _db.RefreshTokens
            .Where(t => t.TokenHash == tokenHash && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, now), cancellationToken);
    }

    public async Task<AuthResult<UserDto>> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken) {
        if (userId == Guid.Empty) {
            return AuthResult<UserDto>.Failure(AuthErrors.Unauthenticated());
        }

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);

        if (user is null) {
            return AuthResult<UserDto>.Failure(AuthErrors.Unauthenticated("Authenticated user no longer exists."));
        }

        if (user.Status == AccountStatus.Banned) {
            return AuthResult<UserDto>.Failure(AuthErrors.AccountBanned());
        }

        return AuthResult<UserDto>.Success(ToUserDto(user));
    }

    public async Task<AuthResult<AccessStatusDto>> GetUserStatusAsync(Guid userId, CancellationToken cancellationToken) {
        if (userId == Guid.Empty) {
            return AuthResult<AccessStatusDto>.Failure(AuthErrors.UserNotFound());
        }

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);

        if (user is null) {
            return AuthResult<AccessStatusDto>.Failure(AuthErrors.UserNotFound());
        }

        return AuthResult<AccessStatusDto>.Success(ToAccessStatusDto(user));
    }

    public async Task<AuthResult<AccessStatusDto>> UpdateUserStatusAsync(
        Guid userId,
        InternalModerationRequest request,
        CancellationToken cancellationToken
    ) {
        var validationErrors = ValidateModerationRequest(userId, request, out var trimmedReason);
        if (validationErrors.Length > 0) {
            return AuthResult<AccessStatusDto>.Failure(
                AuthErrors.ValidationError("One or more validation errors occurred.", validationErrors)
            );
        }

        var operationId = request.OperationId!.Value;
        var actorId = request.ActorId!.Value;
        var targetStatus = request.Status!.Value;
        var fingerprint = ComputeModerationFingerprint(userId, actorId, targetStatus, trimmedReason);
        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () => {
            for (var attempt = 1; attempt <= MaxConcurrencyRetries; attempt++) {
                _db.ChangeTracker.Clear();

                await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                try {
                    var existingOperation = await _db.ModerationOperations
                        .AsNoTracking()
                        .Include(mo => mo.TargetUser)
                        .FirstOrDefaultAsync(mo => mo.OperationId == operationId, cancellationToken);

                    if (existingOperation is not null) {
                        if (!string.Equals(existingOperation.RequestFingerprint, fingerprint, StringComparison.Ordinal)) {
                            await transaction.RollbackAsync(cancellationToken);
                            return AuthResult<AccessStatusDto>.Failure(AuthErrors.IdempotencyConflict());
                        }

                        await transaction.CommitAsync(cancellationToken);
                        return AuthResult<AccessStatusDto>.Success(new AccessStatusDto(
                            existingOperation.TargetUserId,
                            existingOperation.TargetUser.Role,
                            existingOperation.TargetStatus,
                            existingOperation.TargetUser.Role == UserRole.Player && existingOperation.TargetStatus == AccountStatus.Active,
                            existingOperation.ResultVersion
                        ));
                    }

                    var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
                    if (user is null) {
                        await transaction.RollbackAsync(cancellationToken);
                        return AuthResult<AccessStatusDto>.Failure(AuthErrors.UserNotFound());
                    }

                    if (user.Role != UserRole.Player) {
                        await transaction.RollbackAsync(cancellationToken);
                        return AuthResult<AccessStatusDto>.Failure(AuthErrors.InvalidAccountTarget());
                    }

                    var now = DateTimeOffset.UtcNow;
                    user.Status = targetStatus;
                    user.Version++;

                    if (targetStatus == AccountStatus.Banned) {
                        await _db.RefreshTokens
                            .Where(rt => rt.UserId == user.UserId && rt.RevokedAt == null)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(rt => rt.RevokedAt, now), cancellationToken);
                    }

                    _db.ModerationOperations.Add(new ModerationOperation {
                        OperationId = operationId,
                        TargetUserId = user.UserId,
                        ActorId = actorId,
                        TargetStatus = targetStatus,
                        Reason = trimmedReason,
                        RequestFingerprint = fingerprint,
                        ResultVersion = user.Version,
                        CreatedAt = now
                    });

                    await _db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return AuthResult<AccessStatusDto>.Success(ToAccessStatusDto(user));
                } catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyRetries) {
                    await transaction.RollbackAsync(cancellationToken);
                } catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex) && attempt < MaxConcurrencyRetries) {
                    await transaction.RollbackAsync(cancellationToken);
                }
            }

            return AuthResult<AccessStatusDto>.Failure(
                AuthErrors.DependencyUnavailable("Could not complete account moderation due to concurrent updates.")
            );
        });
    }

    private TokenResponse IssueTokens(User user, DateTimeOffset now) {
        var accessToken = _tokenService.GenerateAccessToken(user, now);
        var refreshToken = _tokenService.GenerateRefreshToken(now);

        _db.RefreshTokens.Add(new RefreshToken {
            TokenId = Guid.NewGuid(),
            UserId = user.UserId,
            TokenHash = refreshToken.TokenHash,
            ExpiresAt = refreshToken.ExpiresAt,
            RevokedAt = null
        });

        return new TokenResponse(
            accessToken.Token,
            refreshToken.RawToken,
            "Bearer",
            accessToken.ExpiresInSeconds,
            ToUserDto(user)
        );
    }

    private static UserDto ToUserDto(User user) {
        return new UserDto(
            user.UserId,
            user.Email,
            user.DisplayName,
            user.Role,
            user.Status,
            user.Version
        );
    }

    private static AccessStatusDto ToAccessStatusDto(User user) {
        return new AccessStatusDto(
            user.UserId,
            user.Role,
            user.Status,
            user.Role == UserRole.Player && user.Status == AccountStatus.Active,
            user.Version
        );
    }

    private static string[] ValidateModerationRequest(
        Guid userId,
        InternalModerationRequest request,
        out string trimmedReason
    ) {
        var errors = new List<string>();
        trimmedReason = (request.Reason ?? string.Empty).Trim();

        if (userId == Guid.Empty) {
            errors.Add("userId must be a valid non-empty UUID.");
        }

        if (request.OperationId is null || request.OperationId == Guid.Empty) {
            errors.Add("operationId must be a valid non-empty UUID.");
        }

        if (request.ActorId is null || request.ActorId == Guid.Empty) {
            errors.Add("actorId must be a valid non-empty UUID.");
        }

        if (request.Status is null || !Enum.IsDefined(request.Status.Value)) {
            errors.Add("status must be one of: Active, TradeRestricted, Banned.");
        }

        if (trimmedReason.Length == 0 || trimmedReason.Length > 500) {
            errors.Add("reason is required and must be between 1 and 500 characters.");
        }

        return errors.ToArray();
    }

    private static string ComputeModerationFingerprint(
        Guid targetUserId,
        Guid actorId,
        AccountStatus targetStatus,
        string trimmedReason
    ) {
        var canonical = $"{targetUserId:D}|{actorId:D}|{targetStatus}|{trimmedReason}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(hash);
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
