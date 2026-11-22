namespace Wallet.API.Application.Exceptions;

public sealed class WalletApiException : Exception
{
    public WalletApiException(
        int statusCode,
        string code,
        string detail,
        string? title = null,
        IReadOnlyList<string>? errors = null)
        : base(detail)
    {
        StatusCode = statusCode;
        Code = code;
        Detail = detail;
        Title = title;
        Errors = errors;
    }

    public int StatusCode { get; }
    public string Code { get; }
    public string Detail { get; }
    public string? Title { get; }
    public IReadOnlyList<string>? Errors { get; }
}