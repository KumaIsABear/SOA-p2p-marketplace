namespace Marketplace.API.Domain.Services;

public readonly record struct PriceBounds(long MinUnitPrice, long MaxUnitPrice);

public readonly record struct TradeAmounts(long GrossGem, long FeeGem, long SellerNetGem);

public static class PriceCalculator
{
    private const long BasisPointsScale = 10_000;

    public static PriceBounds ComputeBounds(long referencePrice, int bandBasisPoints)
    {
        if (referencePrice <= 0)
            throw new ArgumentOutOfRangeException(nameof(referencePrice));
        if (bandBasisPoints is < 0 or > 9_999)
            throw new ArgumentOutOfRangeException(nameof(bandBasisPoints));

        Int128 minNumerator = (Int128)referencePrice * (BasisPointsScale - bandBasisPoints);
        long minPrice = (long)((minNumerator + BasisPointsScale - 1) / BasisPointsScale);
        if (minPrice < 1)
            minPrice = 1;

        Int128 maxNumerator = (Int128)referencePrice * (BasisPointsScale + bandBasisPoints);
        long maxPrice = (long)(maxNumerator / BasisPointsScale);

        return new PriceBounds(minPrice, maxPrice);
    }

    public static bool IsWithinBounds(long unitPrice, long minUnitPrice, long maxUnitPrice) =>
        unitPrice >= minUnitPrice && unitPrice <= maxUnitPrice;

    public static TradeAmounts ComputeTradeAmounts(long unitPrice, int quantity, int feeBasisPoints)
    {
        if (unitPrice <= 0)
            throw new ArgumentOutOfRangeException(nameof(unitPrice));
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        if (feeBasisPoints is < 0 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(feeBasisPoints));

        long grossGem = checked(unitPrice * quantity);
        long feeGem = (long)((Int128)grossGem * feeBasisPoints / BasisPointsScale);
        long sellerNetGem = checked(grossGem - feeGem);

        return new TradeAmounts(grossGem, feeGem, sellerNetGem);
    }
}
