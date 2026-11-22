namespace Wallet.API.Infrastructure.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "marketplace-auth";
    public string Audience { get; set; } = "marketplace-api";
    public string SigningKey { get; set; } = string.Empty;
}