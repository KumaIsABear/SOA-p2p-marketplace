using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Wallet.API.Application.DTOs;
using Wallet.API.Application.Exceptions;
using Wallet.API.Domain.Entities;
using Wallet.API.Domain.Enums;
using Wallet.API.Domain.Errors;
using Wallet.API.Infrastructure.Metrics;
using Wallet.API.Infrastructure.Options;
using Wallet.API.Infrastructure.Payments;
using Wallet.API.Infrastructure.Persistence;

namespace Wallet.API.Application.Services;

public sealed class WalletTopUpService : IWalletTopUpService
{
    private const string CreateTopUpOperation = "WAL-04:CreateTopUp";
    private const string CreateTopUpResourceScope = "wallet:top-ups";
    private const int MaxIdempotencyKeyLength = 128;

    private static readonly JsonSerializerOptions ResponseSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions CallbackDataJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly WalletDbContext _dbContext;
    private readonly TopUpPackagesOptions _packagesOptions;
    private readonly ZaloPayOptions _zaloPayOptions;
    private readonly IZaloPayClient _zaloPayClient;
    private readonly ITopUpSettlementService _settlementService;
    private readonly IWalletMetrics _metrics;
    private readonly ILogger<WalletTopUpService> _logger;

    public WalletTopUpService(
        WalletDbContext dbContext,
        IOptions<TopUpPackagesOptions> packagesOptions,
        IOptions<ZaloPayOptions> zaloPayOptions,
        IZaloPayClient zaloPayClient,
        ITopUpSettlementService settlementService,
        IWalletMetrics metrics,
        ILogger<WalletTopUpService> logger)
    {
        _dbContext = dbContext;
        _packagesOptions = packagesOptions.Value;
        _zaloPayOptions = zaloPayOptions.Value;
        _zaloPayClient = zaloPayClient;
        _settlementService = settlementService;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<CreateTopUpResult> CreateTopUpAsync(
        Guid userId,
        string? idempotencyKey,
        CreateTopUpRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedKey = idempotencyKey?.Trim();
        var validationErrors = new List<string>();

        if (string.IsNullOrEmpty(normalizedKey) || normalizedKey.Length > MaxIdempotencyKeyLength)
        {
            validationErrors.Add($"Idempotency-Key header is required and must be between 1 and {MaxIdempotencyKeyLength} characters.");
        }

        var normalizedPackageId = request?.PackageId?.Trim();
        if (string.IsNullOrEmpty(normalizedPackageId))
        {
            validationErrors.Add("packageId is required.");
        }

        if (validationErrors.Count > 0)
        {
            throw new WalletApiException(
                StatusCodes.Status400BadRequest,
                WalletErrorCodes.ValidationError,
                "One or more validation errors occurred.",
                errors: validationErrors);
        }

        var package = _packagesOptions.Items.FirstOrDefault(
            p => string.Equals(p.PackageId.Trim(), normalizedPackageId, StringComparison.OrdinalIgnoreCase));

        if (package is null)
        {
            throw new WalletApiException(
                StatusCodes.Status404NotFound,
                WalletErrorCodes.PackageNotFound,
                $"Top-up package '{normalizedPackageId}' was not found.");
        }

        var canonicalPackageId = package.PackageId.Trim();
        var actorId = userId.ToString("D");
        var fingerprint = ComputeSha256Hex(canonicalPackageId.ToLowerInvariant());

        // TX 1: Idempotency check + lazy-init wallet + persist Pending order & idempotency record
        var phase1 = await PersistPendingOrderOrReplayAsync(
            userId,
            actorId,
            normalizedKey!,
            fingerprint,
            package.AmountVnd,
            package.GemAmount,
            cancellationToken);

        if (phase1.ReplayResult is not null)
        {
            return phase1.ReplayResult;
        }

        var createdOrder = phase1.CreatedOrder!;
        var idempotencyRecordId = phase1.IdempotencyRecordId!.Value;
        var location = BuildOrderLocation(createdOrder.TopUpId);

        // External call OUTSIDE any SQL transaction
        var createRequest = new ZaloPayCreateOrderRequest(
            AppTransId: createdOrder.AppTransId,
            UserId: userId,
            AppTimeMs: createdOrder.CreatedAt.ToUnixTimeMilliseconds(),
            AmountVnd: createdOrder.AmountVnd,
            GemAmount: createdOrder.GemAmount,
            PackageId: canonicalPackageId,
            Description: $"Top up {createdOrder.GemAmount} Gem ({canonicalPackageId}) - {createdOrder.AppTransId}");

        var providerResult = await _zaloPayClient.CreateOrderAsync(createRequest, cancellationToken);

        // TX 2: Lock order row and apply create outcome without ever overwriting Paid
        return await FinalizeCreateOrderAsync(
            createdOrder.TopUpId,
            idempotencyRecordId,
            location,
            providerResult,
            cancellationToken);
    }

    public async Task<TopUpOrderResponse> GetTopUpByIdAsync(
        Guid userId,
        Guid topUpId,
        CancellationToken cancellationToken)
    {
        var order = await _dbContext.TopUpOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.TopUpId == topUpId && o.UserId == userId, cancellationToken);

        if (order is null)
        {
            throw new WalletApiException(
                StatusCodes.Status404NotFound,
                WalletErrorCodes.TopUpNotFound,
                $"Top-up order '{topUpId:D}' was not found.");
        }

        return ToResponse(order);
    }

    public async Task<ZaloPayCallbackResponse> HandleZaloPayCallbackAsync(
        ZaloPayCallbackRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.Data)
            || string.IsNullOrWhiteSpace(request.Mac)
            || request.Type != 1)
        {
            _metrics.RecordPaymentCallback(WalletMetrics.CallbackOutcomes.Invalid);
            return new ZaloPayCallbackResponse
            {
                ReturnCode = -1,
                ReturnMessage = "invalid callback payload or unsupported type"
            };
        }

        if (!_zaloPayOptions.IsConfiguredForCallback())
        {
            _logger.LogWarning("ZaloPay callback received while Key2/AppId configuration is missing.");
            _metrics.RecordPaymentCallback(WalletMetrics.CallbackOutcomes.Retry);
            return new ZaloPayCallbackResponse
            {
                ReturnCode = 0,
                ReturnMessage = "payment configuration unavailable"
            };
        }

        // Verify MAC Key2 over exact original data string BEFORE JSON deserialization
        if (!_zaloPayClient.VerifyCallbackMac(request.Data, request.Mac))
        {
            _logger.LogWarning("ZaloPay callback rejected due to invalid MAC signature.");
            _metrics.RecordPaymentCallback(WalletMetrics.CallbackOutcomes.Invalid);
            return new ZaloPayCallbackResponse
            {
                ReturnCode = -1,
                ReturnMessage = "mac not equal"
            };
        }

        ZaloPayCallbackDataPayload? dataPayload;
        try
        {
            dataPayload = JsonSerializer.Deserialize<ZaloPayCallbackDataPayload>(
                request.Data,
                CallbackDataJsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Signed ZaloPay callback contains unparseable data JSON.");
            _metrics.RecordPaymentCallback(WalletMetrics.CallbackOutcomes.Retry);
            return new ZaloPayCallbackResponse
            {
                ReturnCode = 0,
                ReturnMessage = "invalid callback data payload"
            };
        }

        if (dataPayload is null
            || dataPayload.AppId != _zaloPayOptions.AppId
            || string.IsNullOrWhiteSpace(dataPayload.AppTransId)
            || !Guid.TryParse(dataPayload.AppUser, out var callbackUserId)
            || dataPayload.Amount <= 0
            || dataPayload.ZpTransId <= 0)
        {
            _logger.LogWarning(
                "Signed ZaloPay callback failed field validation. AppIdMatches={AppIdMatches}, AppTransId={AppTransId}, Amount={Amount}, ZpTransId={ZpTransId}.",
                dataPayload?.AppId == _zaloPayOptions.AppId,
                dataPayload?.AppTransId,
                dataPayload?.Amount,
                dataPayload?.ZpTransId);

            _metrics.RecordPaymentCallback(WalletMetrics.CallbackOutcomes.Retry);
            return new ZaloPayCallbackResponse
            {
                ReturnCode = 0,
                ReturnMessage = "callback data validation mismatch"
            };
        }

        try
        {
            var settlement = await _settlementService.SettleTopUpAsync(
                new TopUpSettlementRequest(
                    AppTransId: dataPayload.AppTransId.Trim(),
                    ZpTransId: dataPayload.ZpTransId,
                    AmountVnd: dataPayload.Amount,
                    ExpectedUserId: callbackUserId,
                    Source: "callback"),
                cancellationToken);

            switch (settlement.Outcome)
            {
                case TopUpSettlementOutcome.Settled:
                    _metrics.RecordPaymentCallback(WalletMetrics.CallbackOutcomes.Accepted);
                    return new ZaloPayCallbackResponse
                    {
                        ReturnCode = 1,
                        ReturnMessage = "success"
                    };

                case TopUpSettlementOutcome.Duplicate:
                    _metrics.RecordPaymentCallback(WalletMetrics.CallbackOutcomes.Duplicate);
                    return new ZaloPayCallbackResponse
                    {
                        ReturnCode = 2,
                        ReturnMessage = "duplicate transaction"
                    };

                case TopUpSettlementOutcome.OrderNotFound:
                case TopUpSettlementOutcome.Discrepancy:
                default:
                    _metrics.RecordPaymentCallback(WalletMetrics.CallbackOutcomes.Retry);
                    return new ZaloPayCallbackResponse
                    {
                        ReturnCode = 0,
                        ReturnMessage = "requires reconciliation"
                    };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Transient or unexpected error while settling ZaloPay callback for AppTransId={AppTransId}, ZpTransId={ZpTransId}.",
                dataPayload.AppTransId,
                dataPayload.ZpTransId);

            _metrics.RecordPaymentCallback(WalletMetrics.CallbackOutcomes.Retry);
            return new ZaloPayCallbackResponse
            {
                ReturnCode = 0,
                ReturnMessage = "temporary error, please retry"
            };
        }
    }

    private async Task<Phase1Result> PersistPendingOrderOrReplayAsync(
        Guid userId,
        string actorId,
        string idempotencyKey,
        string requestFingerprint,
        long amountVnd,
        long gemAmount,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction = null;
        if (_dbContext.Database.CurrentTransaction is null && _dbContext.Database.IsRelational())
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            var existingRecord = await FindIdempotencyRecordAsync(
                actorId,
                idempotencyKey,
                cancellationToken);

            if (existingRecord is not null)
            {
                var replay = await BuildReplayResultAsync(existingRecord, requestFingerprint, cancellationToken);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return new Phase1Result(replay, null, null);
            }

            if (!_zaloPayOptions.IsConfiguredForCreate())
            {
                _logger.LogWarning("WAL-04 rejected because ZaloPay create configuration is incomplete.");
                throw new WalletApiException(
                    StatusCodes.Status503ServiceUnavailable,
                    WalletErrorCodes.DependencyUnavailable,
                    "Payment provider is currently unavailable.");
            }

            await EnsureWalletExistsAsync(userId, cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var topUpId = Guid.NewGuid();
            var appTransId = _zaloPayClient.GenerateAppTransId(topUpId, now);
            var location = BuildOrderLocation(topUpId);

            var order = new TopUpOrder
            {
                TopUpId = topUpId,
                UserId = userId,
                Provider = TopUpOrder.DefaultProvider,
                AppTransId = appTransId,
                PaymentUrl = null,
                ProviderTxnId = null,
                AmountVnd = amountVnd,
                GemAmount = gemAmount,
                Status = TopUpOrderStatus.Pending,
                CreatedAt = now,
                PaidAt = null
            };

            var idempotencyRecord = new HttpIdempotencyRecord
            {
                Id = Guid.NewGuid(),
                ActorId = actorId,
                Operation = CreateTopUpOperation,
                ResourceScope = CreateTopUpResourceScope,
                IdempotencyKey = idempotencyKey,
                RequestFingerprint = requestFingerprint,
                StatusCode = StatusCodes.Status202Accepted,
                ResponseBody = SerializeOrder(order),
                LocationHeader = location,
                ResourceId = topUpId,
                CreatedAt = now
            };

            _dbContext.TopUpOrders.Add(order);
            _dbContext.HttpIdempotencyRecords.Add(idempotencyRecord);

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new Phase1Result(null, order, idempotencyRecord.Id);
        }
        catch (DbUpdateException ex) when (IsUniqueKeyViolation(ex))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                await transaction.DisposeAsync();
                transaction = null;
            }

            _dbContext.ChangeTracker.Clear();

            var racedRecord = await FindIdempotencyRecordAsync(
                actorId,
                idempotencyKey,
                cancellationToken);

            if (racedRecord is not null)
            {
                var replay = await BuildReplayResultAsync(racedRecord, requestFingerprint, cancellationToken);
                return new Phase1Result(replay, null, null);
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private async Task<CreateTopUpResult> FinalizeCreateOrderAsync(
        Guid topUpId,
        Guid idempotencyRecordId,
        string location,
        ZaloPayCreateOrderResult providerResult,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction = null;
        if (_dbContext.Database.CurrentTransaction is null && _dbContext.Database.IsRelational())
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            var order = await LockTopUpOrderByIdAsync(topUpId, cancellationToken)
                ?? throw new InvalidOperationException($"Persisted top-up order '{topUpId:D}' could not be reloaded.");

            var idempotencyRecord = await _dbContext.HttpIdempotencyRecords
                .FirstOrDefaultAsync(r => r.Id == idempotencyRecordId, cancellationToken);

            switch (providerResult.Outcome)
            {
                case ZaloPayCreateOutcome.Created:
                    {
                        if (order.Status == TopUpOrderStatus.Pending && !string.IsNullOrWhiteSpace(providerResult.OrderUrl))
                        {
                            order.PaymentUrl = providerResult.OrderUrl;
                        }

                        if (idempotencyRecord is not null)
                        {
                            idempotencyRecord.StatusCode = StatusCodes.Status201Created;
                            idempotencyRecord.ResponseBody = SerializeOrder(order);
                        }

                        await _dbContext.SaveChangesAsync(cancellationToken);
                        if (transaction is not null)
                        {
                            await transaction.CommitAsync(cancellationToken);
                        }

                        return new CreateTopUpResult(StatusCodes.Status201Created, location, ToResponse(order));
                    }

                case ZaloPayCreateOutcome.Rejected:
                    {
                        if (order.Status == TopUpOrderStatus.Paid)
                        {
                            if (idempotencyRecord is not null)
                            {
                                idempotencyRecord.StatusCode = StatusCodes.Status201Created;
                                idempotencyRecord.ResponseBody = SerializeOrder(order);
                            }

                            await _dbContext.SaveChangesAsync(cancellationToken);
                            if (transaction is not null)
                            {
                                await transaction.CommitAsync(cancellationToken);
                            }

                            return new CreateTopUpResult(StatusCodes.Status201Created, location, ToResponse(order));
                        }

                        if (order.Status == TopUpOrderStatus.Pending)
                        {
                            order.Status = TopUpOrderStatus.Failed;
                        }

                        if (idempotencyRecord is not null)
                        {
                            idempotencyRecord.StatusCode = StatusCodes.Status202Accepted;
                            idempotencyRecord.ResponseBody = SerializeOrder(order);
                        }

                        await _dbContext.SaveChangesAsync(cancellationToken);
                        if (transaction is not null)
                        {
                            await transaction.CommitAsync(cancellationToken);
                        }

                        throw new WalletApiException(
                            StatusCodes.Status502BadGateway,
                            WalletErrorCodes.PaymentCreateRejected,
                            "Payment provider rejected top-up order creation.");
                    }

                case ZaloPayCreateOutcome.Ambiguous:
                default:
                    {
                        var responseStatusCode = DetermineReplayStatusCode(order);

                        if (idempotencyRecord is not null)
                        {
                            idempotencyRecord.StatusCode = responseStatusCode;
                            idempotencyRecord.ResponseBody = SerializeOrder(order);
                        }

                        await _dbContext.SaveChangesAsync(cancellationToken);
                        if (transaction is not null)
                        {
                            await transaction.CommitAsync(cancellationToken);
                        }

                        return new CreateTopUpResult(responseStatusCode, location, ToResponse(order));
                    }
            }
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private Task<HttpIdempotencyRecord?> FindIdempotencyRecordAsync(
        string actorId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return _dbContext.HttpIdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.ActorId == actorId
                    && r.Operation == CreateTopUpOperation
                    && r.ResourceScope == CreateTopUpResourceScope
                    && r.IdempotencyKey == idempotencyKey,
                cancellationToken);
    }

    private async Task<CreateTopUpResult> BuildReplayResultAsync(
        HttpIdempotencyRecord existingRecord,
        string incomingFingerprint,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(existingRecord.RequestFingerprint, incomingFingerprint, StringComparison.Ordinal))
        {
            throw new WalletApiException(
                StatusCodes.Status409Conflict,
                WalletErrorCodes.IdempotencyConflict,
                "Idempotency-Key was already used with a different request payload.");
        }

        if (!existingRecord.ResourceId.HasValue)
        {
            throw new InvalidOperationException(
                $"Idempotency record '{existingRecord.Id:D}' is missing ResourceId.");
        }

        var existingOrder = await _dbContext.TopUpOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.TopUpId == existingRecord.ResourceId.Value, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Top-up order '{existingRecord.ResourceId.Value:D}' referenced by idempotency record was not found.");

        var statusCode = DetermineReplayStatusCode(existingOrder);
        var location = string.IsNullOrWhiteSpace(existingRecord.LocationHeader)
            ? BuildOrderLocation(existingOrder.TopUpId)
            : existingRecord.LocationHeader;

        return new CreateTopUpResult(statusCode, location, ToResponse(existingOrder));
    }

    private static int DetermineReplayStatusCode(TopUpOrder order)
    {
        if (!string.IsNullOrWhiteSpace(order.PaymentUrl) || order.Status == TopUpOrderStatus.Paid)
        {
            return StatusCodes.Status201Created;
        }

        return StatusCodes.Status202Accepted;
    }

    private Task<TopUpOrder?> LockTopUpOrderByIdAsync(
        Guid topUpId,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.IsSqlServer())
        {
            return _dbContext.TopUpOrders
                .FromSqlInterpolated(
                    $"""
                    SELECT TopUpId, UserId, Provider, AppTransId, PaymentUrl, ProviderTxnId, AmountVnd, GemAmount, Status, CreatedAt, PaidAt
                    FROM dbo.TopUpOrders WITH (UPDLOCK, ROWLOCK)
                    WHERE TopUpId = {topUpId}
                    """)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return _dbContext.TopUpOrders
            .FirstOrDefaultAsync(o => o.TopUpId == topUpId, cancellationToken);
    }

    private async Task EnsureWalletExistsAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (_dbContext.Database.IsSqlServer())
        {
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                IF NOT EXISTS (SELECT 1 FROM dbo.Wallets WITH (UPDLOCK, HOLDLOCK) WHERE UserId = {userId})
                BEGIN
                    INSERT INTO dbo.Wallets (UserId, TotalBalance, HoldBalance, Version)
                    VALUES ({userId}, 0, 0, 1);
                END
                """,
                cancellationToken);
            return;
        }

        var exists = await _dbContext.Wallets
            .AnyAsync(w => w.UserId == userId, cancellationToken);

        if (!exists)
        {
            _dbContext.Wallets.Add(new Domain.Entities.Wallet
            {
                UserId = userId,
                TotalBalance = 0L,
                HoldBalance = 0L,
                Version = 1L
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static TopUpOrderResponse ToResponse(TopUpOrder order) =>
        new(
            order.TopUpId,
            order.Provider,
            order.AppTransId,
            order.ProviderTxnId,
            order.AmountVnd,
            order.GemAmount,
            order.Status,
            order.PaymentUrl,
            order.CreatedAt.ToUniversalTime(),
            order.PaidAt?.ToUniversalTime());

    private static string SerializeOrder(TopUpOrder order) =>
        JsonSerializer.Serialize(ToResponse(order), ResponseSerializerOptions);

    private static string BuildOrderLocation(Guid topUpId) =>
        $"/api/v1/wallet/top-ups/{topUpId:D}";

    private static string ComputeSha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }

    private static bool IsUniqueKeyViolation(DbUpdateException ex)
    {
        if (ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2627 || sqlEx.Number == 2601))
        {
            return true;
        }

        return ex.InnerException?.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true;
    }

    private sealed record Phase1Result(
        CreateTopUpResult? ReplayResult,
        TopUpOrder? CreatedOrder,
        Guid? IdempotencyRecordId);
}