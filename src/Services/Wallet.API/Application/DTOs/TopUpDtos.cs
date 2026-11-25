using System.Text.Json.Serialization;
using Wallet.API.Domain.Enums;

namespace Wallet.API.Application.DTOs;

public sealed class CreateTopUpRequest
{
    public string? PackageId { get; init; }
}

public sealed record TopUpOrderResponse(
    Guid TopUpId,
    string Provider,
    string AppTransId,
    long? ProviderTxnId,
    long AmountVnd,
    long GemAmount,
    TopUpOrderStatus Status,
    string? PaymentUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt
);

public sealed class ZaloPayCallbackRequest
{
    [JsonPropertyName("data")]
    public string? Data { get; init; }

    [JsonPropertyName("mac")]
    public string? Mac { get; init; }

    [JsonPropertyName("type")]
    public int? Type { get; init; }
}

public sealed class ZaloPayCallbackResponse
{
    [JsonPropertyName("return_code")]
    public int ReturnCode { get; init; }

    [JsonPropertyName("return_message")]
    public string ReturnMessage { get; init; } = string.Empty;
}

public sealed class ZaloPayCallbackDataPayload
{
    [JsonPropertyName("app_id")]
    public int AppId { get; init; }

    [JsonPropertyName("app_trans_id")]
    public string? AppTransId { get; init; }

    [JsonPropertyName("app_time")]
    public long AppTime { get; init; }

    [JsonPropertyName("app_user")]
    public string? AppUser { get; init; }

    [JsonPropertyName("amount")]
    public long Amount { get; init; }

    [JsonPropertyName("embed_data")]
    public string? EmbedData { get; init; }

    [JsonPropertyName("item")]
    public string? Item { get; init; }

    [JsonPropertyName("zp_trans_id")]
    public long ZpTransId { get; init; }

    [JsonPropertyName("server_time")]
    public long ServerTime { get; init; }

    [JsonPropertyName("channel")]
    public int Channel { get; init; }

    [JsonPropertyName("merchant_user_id")]
    public string? MerchantUserId { get; init; }

    [JsonPropertyName("user_fee_amount")]
    public long? UserFeeAmount { get; init; }

    [JsonPropertyName("discount_amount")]
    public long? DiscountAmount { get; init; }
}