using Admin.API.Application.Common;

namespace Admin.API.Tests;

public sealed class PriceBoundsCalculatorTests {
    [Fact]
    public void Ut01_Reference1000Band2000_Returns800To1200Inclusive() {
        var ok = PriceBoundsCalculator.TryComputeBounds(1000L, 2000, out var min, out var max);

        Assert.True(ok);
        Assert.Equal(800L, min);
        Assert.Equal(1200L, max);
    }

    [Fact]
    public void Ut02_Reference101Band2000_Returns81To121Inclusive() {
        var ok = PriceBoundsCalculator.TryComputeBounds(101L, 2000, out var min, out var max);

        Assert.True(ok);
        Assert.Equal(81L, min);
        Assert.Equal(121L, max);
    }

    [Theory]
    [InlineData(1L, 9999, 1L, 1L)]
    [InlineData(500L, 0, 500L, 500L)]
    public void ValidEdgeCases_ReturnExpectedBounds(long reference, int bandBps, long expectedMin, long expectedMax) {
        var ok = PriceBoundsCalculator.TryComputeBounds(reference, bandBps, out var min, out var max);

        Assert.True(ok);
        Assert.Equal(expectedMin, min);
        Assert.Equal(expectedMax, max);
    }

    [Theory]
    [InlineData(0L, 2000)]
    [InlineData(-10L, 2000)]
    [InlineData(1000L, -1)]
    [InlineData(1000L, 10000)]
    [InlineData(long.MaxValue, 2000)]
    public void InvalidOrOverflowInputs_ReturnFalseWithoutWraparound(long reference, int bandBps) {
        var ok = PriceBoundsCalculator.TryComputeBounds(reference, bandBps, out var min, out var max);

        Assert.False(ok);
        Assert.Equal(0L, min);
        Assert.Equal(0L, max);
    }
}
