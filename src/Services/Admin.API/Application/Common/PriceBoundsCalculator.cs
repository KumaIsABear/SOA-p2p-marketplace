namespace Admin.API.Application.Common;

public static class PriceBoundsCalculator {
    public const int MinBandBasisPoints = 0;
    public const int MaxBandBasisPoints = 9999;
    private const long BasisPointsDenominator = 10000L;

    public static bool TryComputeBounds(
        long referencePrice,
        int bandBasisPoints,
        out long minUnitPrice,
        out long maxUnitPrice
    ) {
        minUnitPrice = 0;
        maxUnitPrice = 0;

        if (referencePrice < 1 || bandBasisPoints < MinBandBasisPoints || bandBasisPoints > MaxBandBasisPoints) {
            return false;
        }

        var ref128 = (Int128)referencePrice;
        var denom128 = (Int128)BasisPointsDenominator;
        var minNumerator = ref128 * (BasisPointsDenominator - bandBasisPoints);
        var min128 = (minNumerator + denom128 - 1) / denom128;
        if (min128 < 1) {
            min128 = 1;
        }

        var maxNumerator = ref128 * (BasisPointsDenominator + bandBasisPoints);
        var max128 = maxNumerator / denom128;

        if (min128 > long.MaxValue || max128 > long.MaxValue || max128 < min128) {
            return false;
        }

        minUnitPrice = (long)min128;
        maxUnitPrice = (long)max128;
        return true;
    }
}
