using BackgroundStudio.Core;
using Xunit;

namespace BackgroundStudio.Tests;

public sealed class MaskMathTests
{
    [Fact]
    public void NormalizeSpansTheByteRange()
    {
        var result = MaskMath.Normalize([2f, 4f, 6f]);
        Assert.Equal([0, 128, 255], result);
    }

    [Fact]
    public void NormalizeReturnsZeroForFlatMask()
    {
        Assert.Equal([0, 0], MaskMath.Normalize([5f, 5f]));
    }

    [Theory]
    [InlineData(0, 0.5, 0.2, 0)]
    [InlineData(255, 0.5, 0.2, 255)]
    [InlineData(128, 0.5, 0.2, 131)]
    public void AdjustUsesSmoothThreshold(
        byte alpha,
        double threshold,
        double softness,
        byte expected)
    {
        Assert.Equal(expected, MaskMath.Adjust(alpha, threshold, softness));
    }
}
