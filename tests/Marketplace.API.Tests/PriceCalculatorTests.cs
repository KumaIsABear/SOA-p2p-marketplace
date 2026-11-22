using Marketplace.API.Domain.Services;

namespace Marketplace.API.Tests;

public sealed class PriceCalculatorTests
{
    [Theory]
    [InlineData(1000, 2000, 800, 1200)]
    [InlineData(1, 2000, 1, 1)]
    [InlineData(101, 2000, 81, 121)]
    public void ComputeBounds_MatchesCeilingMinAndFloorMax(
        long referencePrice, int bandBps, long expectedMin, long expectedMax)
    {
        var bounds = PriceCalculator.ComputeBounds(referencePrice, bandBps);
        Assert.Equal(expectedMin, bounds.MinUnitPrice);
        Assert.Equal(expectedMax, bounds.MaxUnitPrice);
    }

    [Theory]
    [InlineData(1000, 1, 500, 1000, 50, 950)]
    [InlineData(19, 1, 500, 19, 0, 19)]
    [InlineData(333, 3, 500, 999, 49, 950)]
    public void ComputeTradeAmounts_FloorsFeeAndPreservesInvariant(
        long unitPrice, int quantity, int feeBps, long gross, long fee, long net)
    {
        var amounts = PriceCalculator.ComputeTradeAmounts(unitPrice, quantity, feeBps);
        Assert.Equal(gross, amounts.GrossGem);
        Assert.Equal(fee, amounts.FeeGem);
        Assert.Equal(net, amounts.SellerNetGem);
        Assert.Equal(amounts.GrossGem, amounts.FeeGem + amounts.SellerNetGem);
    }

    [Fact]
    public void ComputeTradeAmounts_ThrowsOnOverflow()
    {
        Assert.Throws<OverflowException>(() => PriceCalculator.ComputeTradeAmounts(long.MaxValue, 2, 500));
    }
}
