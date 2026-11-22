namespace Wallet.API.Infrastructure.Options;

public sealed class TopUpPackagesOptions
{
    public const string SectionName = "TopUpPackages";

    public List<TopUpPackageOption> Items { get; set; } = [];

    public void Validate()
    {
        if (Items.Count == 0)
        {
            throw new InvalidOperationException("TopUpPackages:Items must contain at least one package.");
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items)
        {
            if (string.IsNullOrWhiteSpace(item.PackageId))
            {
                throw new InvalidOperationException("TopUpPackages:Items contains a package with an empty PackageId.");
            }

            if (!seenIds.Add(item.PackageId.Trim()))
            {
                throw new InvalidOperationException($"TopUpPackages:Items contains duplicate PackageId '{item.PackageId}'.");
            }

            if (item.AmountVnd <= 0)
            {
                throw new InvalidOperationException($"TopUp package '{item.PackageId}' must have AmountVnd > 0.");
            }

            if (item.GemAmount <= 0)
            {
                throw new InvalidOperationException($"TopUp package '{item.PackageId}' must have GemAmount > 0.");
            }
        }
    }
}

public sealed class TopUpPackageOption
{
    public string PackageId { get; set; } = string.Empty;
    public long AmountVnd { get; set; }
    public long GemAmount { get; set; }
}