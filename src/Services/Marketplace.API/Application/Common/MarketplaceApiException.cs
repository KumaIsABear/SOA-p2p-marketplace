namespace Marketplace.API.Application.Common;

public sealed class MarketplaceApiException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }
    public string[]? Errors { get; }

    public MarketplaceApiException(int statusCode, string code, string detail, string[]? errors = null)
        : base(detail)
    {
        StatusCode = statusCode;
        Code = code;
        Errors = errors;
    }
}
