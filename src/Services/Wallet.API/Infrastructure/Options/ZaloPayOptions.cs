namespace Wallet.API.Infrastructure.Options;

public sealed class ZaloPayOptions
{
    public const string SectionName = "ZaloPay";

    public string BaseUrl { get; set; } = "https://sb-openapi.zalopay.vn";
    public int AppId { get; set; }
    public string Key1 { get; set; } = string.Empty;
    public string Key2 { get; set; } = string.Empty;
    public string CallbackUrl { get; set; } = string.Empty;
    public string? RedirectUrl { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public int ReconcileIntervalSeconds { get; set; } = 60;
    public int OrderExpiryMinutes { get; set; } = 15;
    public bool ReconciliationEnabled { get; set; } = true;

    public bool IsConfiguredForCreate() =>
        AppId > 0
        && !string.IsNullOrWhiteSpace(Key1)
        && TryGetBaseUri(out _)
        && !string.IsNullOrWhiteSpace(CallbackUrl)
        && Uri.TryCreate(CallbackUrl.Trim(), UriKind.Absolute, out _);

    public bool IsConfiguredForQuery() =>
        AppId > 0
        && !string.IsNullOrWhiteSpace(Key1)
        && TryGetBaseUri(out _);

    public bool IsConfiguredForCallback() =>
        AppId > 0
        && !string.IsNullOrWhiteSpace(Key2);

    public bool TryGetBaseUri(out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            return false;
        }

        return Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out uri);
    }

    public int GetEffectiveReconcileIntervalSeconds() =>
        ReconcileIntervalSeconds >= 5 ? ReconcileIntervalSeconds : 60;

    public int GetEffectiveOrderExpiryMinutes() =>
        OrderExpiryMinutes >= 1 ? OrderExpiryMinutes : 15;
}