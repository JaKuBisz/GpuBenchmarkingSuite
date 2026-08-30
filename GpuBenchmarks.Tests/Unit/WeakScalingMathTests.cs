using GpuBenchmarks.Scaling;

namespace GpuBenchmarks.Tests.Unit;

public class WeakScalingMathTests
{
    [Fact]
    public void MatrixMultiply_UsesCubeRootScaling()
    {
        int scaled = WeakScalingMath.ComputeScaledSize(256, 8, "MatrixMultiply");
        Assert.Equal(512, scaled);
    }

    [Fact]
    public void GameOfLife_UsesSqrtScaling()
    {
        int scaled = WeakScalingMath.ComputeScaledSize(256, 4, "GameOfLife");
        Assert.Equal(512, scaled);
    }
}
