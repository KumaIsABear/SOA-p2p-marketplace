using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Admin.API.Application.Common;
using Admin.API.Application.DTOs;
using Admin.API.Application.Interfaces;
using Admin.API.Domain.Entities;
using Admin.API.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Admin.API.Application.Services;

public sealed class SupportService : ISupportService {
    private const string ModerateOperationName = "ADM-07:ModerateUser";
    private const int MaxConcurrencyRetries = 3;

    private static readonly HashSet<string> AllowedTradeStatuses = new(StringComparer.Ordinal) {
        "HoldingFunds",
        "Transferring",
        "Settling",
        "Finalizing",
        "ReversingItem",
        "ReleasingFunds",
        "Completed",
        "Failed"
    };

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AdminDbContext _db;
    private readonly IMarketplaceInternalClient _marketplaceClient;
    private readonly IWalletInternalClient _walletClient;
    private readonly IInventoryInternalClient _inventoryClient;
    private readonly IAuthInternalClient _authClient;

    public SupportService(
        AdminDbContext db,
        IMarketplaceInternalClient marketplaceClient,
        IWalletInternalClient walletClient,
        IInventoryInternalClient inventoryClient,
        IAuthInternalClient authClient
    ) {
        _db = db;
        _marketplaceClient = marketplaceClient;
        _walletClient = walletClient;
        _inventoryClient = inventoryClient;
        _authClient = authClient;
    }

    public async Task<AdminResult<SupportTradeTraceDto>> GetTradeTraceAsync(
        Guid tradeId,
        string correlationId,
        CancellationToken cancellationToken
    ) {
        if (tradeId == Guid.Empty) {
            return AdminResult<SupportTradeTraceDto>.Failure(AdminErrors.TradeNotFound());
        }

        var marketResult = await _marketplaceClient.GetTradeTraceAsync(tradeId, correlationId, cancellationToken);
        if (marketResult.Outcome == InternalCallOutcome.NotFound) {
            return AdminResult<SupportTradeTraceDto>.Failure(AdminErrors.TradeNotFound());
        }

        if (marketResult.Outcome != InternalCallOutcome.Success || marketResult.Value is null) {
            return AdminResult<SupportTradeTraceDto>.Failure(
                AdminErrors.DependencyUnavailable("Marketplace trace service is temporarily unavailable.")
            );
        }

        var walletTask = _walletClient.GetTradeTraceAsync(tradeId, correlationId, cancellationToken);
        var inventoryTask = _inventoryClient.GetTradeTransferTraceAsync(tradeId, correlationId, cancellationToken);

        await Task.WhenAll(walletTask, inventoryTask);

        var walletResult = walletTask.Result;
        var inventoryResult = inventoryTask.Result;

        var unavailableSources = new List<string>();
        WalletTradeTraceDto? walletTrace = null;
        ItemTransferTraceDto? inventoryTrace = null;

        if (walletResult.Outcome == InternalCallOutcome.Success && walletResult.Value is not null) {
            walletTrace = walletResult.Value;
        } else {
            unavailableSources.Add("wallet");
        }

        if (inventoryResult.Outcome == InternalCallOutcome.Success && inventoryResult.Value is not null) {
            inventoryTrace = inventoryResult.Value;
        } else if (inventoryResult.Outcome == InternalCallOutcome.NotFound) {
            inventoryTrace = null;
        } else {
            unavailableSources.Add("inventory");
        }

        var trace = new SupportTradeTraceDto(
            marketResult.Value,
            walletTrace,
            inventoryTrace,
            unavailableSources.Count > 0,
            unavailableSources
        );

        return AdminResult<SupportTradeTraceDto>.Success(trace);
    }

    public async Task<AdminResult<TradePageDto>> SearchTradesAsync(
        Guid? userId,
        string? status,
        int page,
        int pageSize,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string correlationId,
        CancellationToken cancellationToken
    ) {
        var errors = new List<string>();

        if (userId is null || userId == Guid.Empty) {
            errors.Add("userId query parameter is required and must be a valid non-empty UUID.");
        }

        if (!string.IsNullOrWhiteSpace(status) && !AllowedTradeStatuses.Contains(status.Trim())) {
            errors.Add("status must be one of: HoldingFunds, Transferring, Settling, Finalizing, ReversingItem, ReleasingFunds, Completed, Failed.");
        }

        if (page < 1) {
            errors.Add("page must be greater than or equal to 1.");
        }

        if (pageSize < 1 || pageSize > 100) {
            errors.Add("pageSize must be between 1 and 100.");
        }

        if (from.HasValue && to.HasValue && from.Value >= to.Value) {
            errors.Add("from must be strictly earlier than to.");
        }

        if (errors.Count > 0) {
            return AdminResult<TradePageDto>.Failure(
                AdminErrors.ValidationError("One or more validation errors occurred.", errors.ToArray())
            );
        }

        var result = await _marketplaceClient.SearchTradesAsync(
            userId!.Value,
            string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
            page,
            pageSize,
            from,
            to,
            correlationId,
            cancellationToken
        );

        if (result.Outcome == InternalCallOutcome.Success && result.Value is not null) {
            return AdminResult<TradePageDto>.Success(result.Value);
        }

        if (result.Outcome == InternalCallOutcome.BadRequest) {
            return AdminResult<TradePageDto>.Failure(
                AdminErrors.ValidationError(result.ErrorDetail ?? "Invalid trade search parameters.")
            );
        }

        return AdminResult<TradePageDto>.Failure(
            AdminErrors.DependencyUnavailable("Marketplace trade search service is temporarily unavailable.")
        );
    }

    public async Task<AdminResult<ModerationResultDto>> ModerateUserAsync(
        Guid targetUserId,
        Guid actorId,
        string idempotencyKey,
        ModerationRequest request,
        string correlationId,
        CancellationToken cancellationToken
    ) {
        var errors = ValidateModerationRequest(targetUserId, actorId, idempotencyKey, request, out var trimmedKey, out var trimmedReason);
        if (errors.Length > 0) {
            return AdminResult<ModerationResultDto>.Failure(
                AdminErrors.ValidationError("One or more validation errors occurred.", errors)
            );
        }

        var targetStatus = request.Status!.Value;
        var actorKey = actorId.ToString("D");
        var resourceScope = $"user:{targetUserId:D}";
        var fingerprint = ComputeModerationFingerprint(targetUserId, targetStatus, trimmedReason);

        var prep = await PrepareModerationIntentAsync(
            targetUserId,
            actorId,
            actorKey,
            resourceScope,
            trimmedKey,
            fingerprint,
            targetStatus,
            trimmedReason,
            cancellationToken
        );

        if (prep.ImmediateResult is not null) {
            return prep.ImmediateResult;
        }

        var actionId = prep.ActionId;
        var operationId = prep.OperationId;

        var authResponse = await _authClient.UpdateUserStatusAsync(
            targetUserId,
            new InternalModerationRequest(operationId, actorId, targetStatus, trimmedReason),
            correlationId,
            cancellationToken
        );

        if (authResponse.Outcome == InternalCallOutcome.Unavailable) {
            return AdminResult<ModerationResultDto>.Failure(
                AdminErrors.DependencyUnavailable("Auth service is temporarily unavailable. Retry with the same Idempotency-Key.")
            );
        }

        var now = DateTimeOffset.UtcNow;

        if (authResponse.Outcome == InternalCallOutcome.Success && authResponse.Value is not null) {
            var dto = new ModerationResultDto(
                actionId,
                targetUserId,
                authResponse.Value.Status,
                authResponse.Value.Version,
                now
            );

            await FinalizeModerationOutcomeAsync(
                actionId,
                actorKey,
                resourceScope,
                trimmedKey,
                "Succeeded",
                StatusCodes.Status200OK,
                JsonSerializer.Serialize(dto, JsonOptions),
                now,
                cancellationToken
            );

            return AdminResult<ModerationResultDto>.Success(dto);
        }

        var mappedError = MapAuthModerationError(authResponse);
        var serializedError = JsonSerializer.Serialize(mappedError, JsonOptions);

        await FinalizeModerationOutcomeAsync(
            actionId,
            actorKey,
            resourceScope,
            trimmedKey,
            "Rejected",
            mappedError.StatusCode,
            serializedError,
            now,
            cancellationToken
        );

        return AdminResult<ModerationResultDto>.Failure(mappedError);
    }

    private sealed record ModerationPrepResult(
        AdminResult<ModerationResultDto>? ImmediateResult,
        Guid ActionId = default,
        Guid OperationId = default
    );

    private async Task<ModerationPrepResult> PrepareModerationIntentAsync(
        Guid targetUserId,
        Guid actorId,
        string actorKey,
        string resourceScope,
        string trimmedKey,
        string fingerprint,
        AccountStatusValue targetStatus,
        string trimmedReason,
        CancellationToken cancellationToken
    ) {
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () => {
            for (var attempt = 1; attempt <= MaxConcurrencyRetries; attempt++) {
                _db.ChangeTracker.Clear();

                await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                try {
                    var existingRecord = await _db.HttpIdempotencyRecords
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            r => r.ActorId == actorKey
                                && r.Operation == ModerateOperationName
                                && r.ResourceScope == resourceScope
                                && r.IdempotencyKey == trimmedKey,
                            cancellationToken
                        );

                    if (existingRecord is not null) {
                        if (!string.Equals(existingRecord.RequestFingerprint, fingerprint, StringComparison.Ordinal)) {
                            await transaction.RollbackAsync(cancellationToken);
                            return new ModerationPrepResult(
                                AdminResult<ModerationResultDto>.Failure(AdminErrors.IdempotencyConflict())
                            );
                        }

                        if (existingRecord.StatusCode == StatusCodes.Status200OK) {
                            var replayDto = JsonSerializer.Deserialize<ModerationResultDto>(existingRecord.ResponseBody, JsonOptions);
                            await transaction.CommitAsync(cancellationToken);
                            if (replayDto is not null) {
                                return new ModerationPrepResult(AdminResult<ModerationResultDto>.Success(replayDto));
                            }
                            return new ModerationPrepResult(
                                AdminResult<ModerationResultDto>.Failure(AdminErrors.InternalError("Stored moderation response could not be read."))
                            );
                        }

                        if (existingRecord.StatusCode >= 400) {
                            var replayErr = JsonSerializer.Deserialize<AdminError>(existingRecord.ResponseBody, JsonOptions);
                            await transaction.CommitAsync(cancellationToken);
                            return new ModerationPrepResult(
                                AdminResult<ModerationResultDto>.Failure(replayErr ?? AdminErrors.InternalError())
                            );
                        }

                        if (existingRecord.ResourceId.HasValue) {
                            var pendingAction = await _db.ModerationActions
                                .AsNoTracking()
                                .FirstOrDefaultAsync(a => a.ActionId == existingRecord.ResourceId.Value, cancellationToken);

                            if (pendingAction is not null) {
                                await transaction.CommitAsync(cancellationToken);
                                return new ModerationPrepResult(null, pendingAction.ActionId, pendingAction.OperationId);
                            }
                        }

                        await transaction.RollbackAsync(cancellationToken);
                        return new ModerationPrepResult(
                            AdminResult<ModerationResultDto>.Failure(AdminErrors.InternalError("Pending moderation record is corrupted."))
                        );
                    }

                    var now = DateTimeOffset.UtcNow;
                    var action = new ModerationAction {
                        ActionId = Guid.NewGuid(),
                        OperationId = Guid.NewGuid(),
                        ActorId = actorId,
                        TargetUserId = targetUserId,
                        Action = $"SetStatus:{targetStatus}",
                        Reason = trimmedReason,
                        Status = "Pending",
                        CreatedAt = now,
                        UpdatedAt = now
                    };

                    var idempotencyRecord = new HttpIdempotencyRecord {
                        Id = Guid.NewGuid(),
                        ActorId = actorKey,
                        Operation = ModerateOperationName,
                        ResourceScope = resourceScope,
                        IdempotencyKey = trimmedKey,
                        RequestFingerprint = fingerprint,
                        StatusCode = 0,
                        ResponseBody = string.Empty,
                        ResourceId = action.ActionId,
                        CreatedAt = now
                    };

                    _db.ModerationActions.Add(action);
                    _db.HttpIdempotencyRecords.Add(idempotencyRecord);

                    await _db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return new ModerationPrepResult(null, action.ActionId, action.OperationId);
                } catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex) && attempt < MaxConcurrencyRetries) {
                    await transaction.RollbackAsync(cancellationToken);
                }
            }

            return new ModerationPrepResult(
                AdminResult<ModerationResultDto>.Failure(AdminErrors.DependencyUnavailable("Could not prepare moderation request due to concurrent updates."))
            );
        });
    }

    private async Task FinalizeModerationOutcomeAsync(
        Guid actionId,
        string actorKey,
        string resourceScope,
        string trimmedKey,
        string auditStatus,
        int httpStatusCode,
        string responseBody,
        DateTimeOffset now,
        CancellationToken cancellationToken
    ) {
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () => {
            _db.ChangeTracker.Clear();

            var action = await _db.ModerationActions.FirstOrDefaultAsync(a => a.ActionId == actionId, cancellationToken);
            if (action is not null) {
                action.Status = auditStatus;
                action.UpdatedAt = now;
            }

            var record = await _db.HttpIdempotencyRecords.FirstOrDefaultAsync(
                r => r.ActorId == actorKey
                    && r.Operation == ModerateOperationName
                    && r.ResourceScope == resourceScope
                    && r.IdempotencyKey == trimmedKey,
                cancellationToken
            );

            if (record is not null) {
                record.StatusCode = httpStatusCode;
                record.ResponseBody = responseBody;
            }

            await _db.SaveChangesAsync(cancellationToken);
        });
    }

    private static AdminError MapAuthModerationError(InternalCallResult<AccessStatusDto> authResponse) {
        return authResponse.Outcome switch {
            InternalCallOutcome.NotFound => AdminErrors.UserNotFound(),
            InternalCallOutcome.Conflict when string.Equals(authResponse.ErrorCode, "INVALID_ACCOUNT_TARGET", StringComparison.Ordinal) =>
                AdminErrors.InvalidAccountTarget(),
            InternalCallOutcome.Conflict when string.Equals(authResponse.ErrorCode, "IDEMPOTENCY_CONFLICT", StringComparison.Ordinal) =>
                AdminErrors.IdempotencyConflict(),
            InternalCallOutcome.Conflict => AdminErrors.InvalidAccountTarget(),
            InternalCallOutcome.BadRequest => AdminErrors.ValidationError(authResponse.ErrorDetail ?? "Invalid moderation payload."),
            _ => AdminErrors.DependencyUnavailable("Auth service rejected or could not complete the moderation request.")
        };
    }

    private static string[] ValidateModerationRequest(
        Guid targetUserId,
        Guid actorId,
        string idempotencyKey,
        ModerationRequest request,
        out string trimmedKey,
        out string trimmedReason
    ) {
        var errors = new List<string>();
        trimmedKey = (idempotencyKey ?? string.Empty).Trim();
        trimmedReason = (request.Reason ?? string.Empty).Trim();

        if (targetUserId == Guid.Empty) {
            errors.Add("userId must be a valid non-empty UUID.");
        }

        if (actorId == Guid.Empty) {
            errors.Add("Authenticated actor subject must be a valid non-empty UUID.");
        }

        if (trimmedKey.Length == 0 || trimmedKey.Length > 128) {
            errors.Add("Idempotency-Key header is required and must be between 1 and 128 characters.");
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
        AccountStatusValue targetStatus,
        string trimmedReason
    ) {
        var canonical = $"{targetUserId:D}|{targetStatus}|{trimmedReason}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(hash);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) {
        return ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627);
    }
}
