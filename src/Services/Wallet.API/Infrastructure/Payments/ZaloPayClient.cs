using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Wallet.API.Infrastructure.Options;

namespace Wallet.API.Infrastructure.Payments;

public sealed class ZaloPayClient : IZaloPayClient
{
    private const int DuplicateAppTransIdSubReturnCode = -68;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly TimeZoneInfo VietnamTimeZone = ResolveVietnamTimeZone();

    private readonly HttpClient _httpClient;
    private readonly ZaloPayOptions _options;
    private readonly ILogger<ZaloPayClient> _logger;

    public ZaloPayClient(
        HttpClient httpClient,
        IOptions<ZaloPayOptions> options,
        ILogger<ZaloPayClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string GenerateAppTransId(Guid topUpId, DateTimeOffset utcNow)
    {
        var vietnamTime = TimeZoneInfo.ConvertTime(utcNow.ToUniversalTime(), VietnamTimeZone);
        // Format yyMMdd_<32 hex chars> = 39 chars (<= ZaloPay 40-char limit)
        return $"{vietnamTime:yyMMdd}_{topUpId:N}";
    }

    public async Task<ZaloPayCreateOrderResult> CreateOrderAsync(
        ZaloPayCreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.IsConfiguredForCreate() || !_options.TryGetBaseUri(out var baseUri) || baseUri is null)
        {
            return new ZaloPayCreateOrderResult(
                ZaloPayCreateOutcome.Ambiguous,
                null,
                null,
                "ZaloPay client is not configured.",
                null,
                null);
        }

        var appId = _options.AppId.ToString(CultureInfo.InvariantCulture);
        var appUser = request.UserId.ToString("D");
        var amount = request.AmountVnd.ToString(CultureInfo.InvariantCulture);
        var appTime = request.AppTimeMs.ToString(CultureInfo.InvariantCulture);

        var embedDataObj = string.IsNullOrWhiteSpace(_options.RedirectUrl)
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>
            {
                ["redirecturl"] = _options.RedirectUrl.Trim()
            };

        var embedData = JsonSerializer.Serialize(embedDataObj);
        var item = JsonSerializer.Serialize(new[]
        {
            new
            {
                packageId = request.PackageId,
                gemAmount = request.GemAmount,
                amountVnd = request.AmountVnd
            }
        });

        var macInput = $"{appId}|{request.AppTransId}|{appUser}|{amount}|{appTime}|{embedData}|{item}";
        var mac = ComputeHmacSha256Hex(_options.Key1, macInput);

        var formFields = new Dictionary<string, string>
        {
            ["app_id"] = appId,
            ["app_trans_id"] = request.AppTransId,
            ["app_user"] = appUser,
            ["app_time"] = appTime,
            ["amount"] = amount,
            ["item"] = item,
            ["embed_data"] = embedData,
            ["description"] = request.Description,
            ["callback_url"] = _options.CallbackUrl.Trim(),
            ["bank_code"] = _options.BankCode?.Trim() ?? string.Empty,
            ["mac"] = mac
        };

        var endpointUri = new Uri(baseUri, "/v2/create");

        try
        {
            using var content = new FormUrlEncodedContent(formFields);
            using var response = await _httpClient.PostAsync(endpointUri, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            var statusCode = (int)response.StatusCode;
            if (statusCode >= 500)
            {
                _logger.LogWarning(
                    "ZaloPay /v2/create returned server error HTTP {StatusCode} for AppTransId={AppTransId}.",
                    statusCode,
                    request.AppTransId);

                return new ZaloPayCreateOrderResult(
                    ZaloPayCreateOutcome.Ambiguous,
                    null,
                    null,
                    $"Provider HTTP {statusCode}",
                    null,
                    null);
            }

            ZaloPayCreateRawResponse? payload;
            try
            {
                payload = JsonSerializer.Deserialize<ZaloPayCreateRawResponse>(responseBody, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to deserialize ZaloPay /v2/create response for AppTransId={AppTransId}. HttpStatus={StatusCode}",
                    request.AppTransId,
                    statusCode);

                return new ZaloPayCreateOrderResult(
                    ZaloPayCreateOutcome.Ambiguous,
                    null,
                    null,
                    "Invalid provider response payload.",
                    null,
                    null);
            }

            if (payload is null)
            {
                return new ZaloPayCreateOrderResult(
                    ZaloPayCreateOutcome.Ambiguous,
                    null,
                    null,
                    "Empty provider response payload.",
                    null,
                    null);
            }

            if (payload.ReturnCode == 1)
            {
                if (!string.IsNullOrWhiteSpace(payload.OrderUrl))
                {
                    return new ZaloPayCreateOrderResult(
                        ZaloPayCreateOutcome.Created,
                        payload.ReturnCode,
                        payload.SubReturnCode,
                        payload.ReturnMessage,
                        payload.SubReturnMessage,
                        payload.OrderUrl.Trim());
                }

                _logger.LogWarning(
                    "ZaloPay /v2/create returned return_code=1 without order_url for AppTransId={AppTransId}.",
                    request.AppTransId);

                return new ZaloPayCreateOrderResult(
                    ZaloPayCreateOutcome.Ambiguous,
                    payload.ReturnCode,
                    payload.SubReturnCode,
                    payload.ReturnMessage,
                    payload.SubReturnMessage,
                    null);
            }

            if (payload.SubReturnCode == DuplicateAppTransIdSubReturnCode)
            {
                _logger.LogWarning(
                    "ZaloPay /v2/create returned duplicate app_trans_id (-68) for AppTransId={AppTransId}.",
                    request.AppTransId);

                return new ZaloPayCreateOrderResult(
                    ZaloPayCreateOutcome.Ambiguous,
                    payload.ReturnCode,
                    payload.SubReturnCode,
                    payload.ReturnMessage,
                    payload.SubReturnMessage,
                    null);
            }

            _logger.LogWarning(
                "ZaloPay /v2/create rejected AppTransId={AppTransId}. ReturnCode={ReturnCode}, SubReturnCode={SubReturnCode}.",
                request.AppTransId,
                payload.ReturnCode,
                payload.SubReturnCode);

            return new ZaloPayCreateOrderResult(
                ZaloPayCreateOutcome.Rejected,
                payload.ReturnCode,
                payload.SubReturnCode,
                payload.ReturnMessage,
                payload.SubReturnMessage,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            _logger.LogWarning(
                ex,
                "Transient transport error calling ZaloPay /v2/create for AppTransId={AppTransId}.",
                request.AppTransId);

            return new ZaloPayCreateOrderResult(
                ZaloPayCreateOutcome.Ambiguous,
                null,
                null,
                "Transport error or timeout while calling payment provider.",
                null,
                null);
        }
    }

    public async Task<ZaloPayQueryOrderResult> QueryOrderAsync(
        string appTransId,
        CancellationToken cancellationToken)
    {
        if (!_options.IsConfiguredForQuery() || !_options.TryGetBaseUri(out var baseUri) || baseUri is null)
        {
            return new ZaloPayQueryOrderResult(
                ZaloPayQueryOutcome.TransientError,
                null,
                null,
                false,
                0L,
                0L,
                "ZaloPay query configuration is missing.");
        }

        var appId = _options.AppId.ToString(CultureInfo.InvariantCulture);
        var macInput = $"{appId}|{appTransId}|{_options.Key1}";
        var mac = ComputeHmacSha256Hex(_options.Key1, macInput);

        var formFields = new Dictionary<string, string>
        {
            ["app_id"] = appId,
            ["app_trans_id"] = appTransId,
            ["mac"] = mac
        };

        var endpointUri = new Uri(baseUri, "/v2/query");

        try
        {
            using var content = new FormUrlEncodedContent(formFields);
            using var response = await _httpClient.PostAsync(endpointUri, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "ZaloPay /v2/query returned HTTP {StatusCode} for AppTransId={AppTransId}.",
                    (int)response.StatusCode,
                    appTransId);

                return new ZaloPayQueryOrderResult(
                    ZaloPayQueryOutcome.TransientError,
                    null,
                    null,
                    false,
                    0L,
                    0L,
                    $"Provider HTTP {(int)response.StatusCode}");
            }

            ZaloPayQueryRawResponse? payload;
            try
            {
                payload = JsonSerializer.Deserialize<ZaloPayQueryRawResponse>(responseBody, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to deserialize ZaloPay /v2/query response for AppTransId={AppTransId}.",
                    appTransId);

                return new ZaloPayQueryOrderResult(
                    ZaloPayQueryOutcome.TransientError,
                    null,
                    null,
                    false,
                    0L,
                    0L,
                    "Invalid provider query response.");
            }

            if (payload is null)
            {
                return new ZaloPayQueryOrderResult(
                    ZaloPayQueryOutcome.TransientError,
                    null,
                    null,
                    false,
                    0L,
                    0L,
                    "Empty provider query response.");
            }

            var outcome = payload.ReturnCode switch
            {
                1 => ZaloPayQueryOutcome.Paid,
                2 when !payload.IsProcessing => ZaloPayQueryOutcome.Failed,
                _ => ZaloPayQueryOutcome.Processing
            };

            return new ZaloPayQueryOrderResult(
                outcome,
                payload.ReturnCode,
                payload.SubReturnCode,
                payload.IsProcessing,
                payload.Amount,
                payload.ZpTransId,
                payload.ReturnMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            _logger.LogWarning(
                ex,
                "Transient transport error calling ZaloPay /v2/query for AppTransId={AppTransId}.",
                appTransId);

            return new ZaloPayQueryOrderResult(
                ZaloPayQueryOutcome.TransientError,
                null,
                null,
                false,
                0L,
                0L,
                "Transport error or timeout while querying payment provider.");
        }
    }

    public bool VerifyCallbackMac(string rawData, string providedMac)
    {
        if (string.IsNullOrEmpty(rawData)
            || string.IsNullOrWhiteSpace(providedMac)
            || string.IsNullOrWhiteSpace(_options.Key2))
        {
            return false;
        }

        var expectedBytes = ComputeHmacSha256Bytes(_options.Key2, rawData);

        byte[] providedBytes;
        try
        {
            providedBytes = Convert.FromHexString(providedMac.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        if (providedBytes.Length != expectedBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    private static byte[] ComputeHmacSha256Bytes(string key, string data)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        return HMACSHA256.HashData(keyBytes, dataBytes);
    }

    private static string ComputeHmacSha256Hex(string key, string data)
    {
        var hash = ComputeHmacSha256Bytes(key, data);
        return Convert.ToHexStringLower(hash);
    }

    private static TimeZoneInfo ResolveVietnamTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            "UTC+07",
            TimeSpan.FromHours(7),
            "(UTC+07:00) Ho Chi Minh",
            "ICT");
    }

    private sealed class ZaloPayCreateRawResponse
    {
        [JsonPropertyName("return_code")]
        public int ReturnCode { get; init; }

        [JsonPropertyName("return_message")]
        public string? ReturnMessage { get; init; }

        [JsonPropertyName("sub_return_code")]
        public int? SubReturnCode { get; init; }

        [JsonPropertyName("sub_return_message")]
        public string? SubReturnMessage { get; init; }

        [JsonPropertyName("order_url")]
        public string? OrderUrl { get; init; }

        [JsonPropertyName("zp_trans_token")]
        public string? ZpTransToken { get; init; }
    }

    private sealed class ZaloPayQueryRawResponse
    {
        [JsonPropertyName("return_code")]
        public int ReturnCode { get; init; }

        [JsonPropertyName("return_message")]
        public string? ReturnMessage { get; init; }

        [JsonPropertyName("sub_return_code")]
        public int? SubReturnCode { get; init; }

        [JsonPropertyName("sub_return_message")]
        public string? SubReturnMessage { get; init; }

        [JsonPropertyName("is_processing")]
        public bool IsProcessing { get; init; }

        [JsonPropertyName("amount")]
        public long Amount { get; init; }

        [JsonPropertyName("zp_trans_id")]
        public long ZpTransId { get; init; }
    }
}