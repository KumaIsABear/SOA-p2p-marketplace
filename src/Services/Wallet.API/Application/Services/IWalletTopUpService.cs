using Wallet.API.Application.DTOs;

namespace Wallet.API.Application.Services;

public sealed record CreateTopUpResult(
    int StatusCode,
    string Location,
    TopUpOrderResponse Order
);

public interface IWalletTopUpService
{
    Task<CreateTopUpResult> CreateTopUpAsync(
        Guid userId,
        string? idempotencyKey,
        CreateTopUpRequest request,
        CancellationToken cancellationToken);

    Task<TopUpOrderResponse> GetTopUpByIdAsync(
        Guid userId,
        Guid topUpId,
        CancellationToken cancellationToken);

    Task<ZaloPayCallbackResponse> HandleZaloPayCallbackAsync(
        ZaloPayCallbackRequest request,
        CancellationToken cancellationToken);
}