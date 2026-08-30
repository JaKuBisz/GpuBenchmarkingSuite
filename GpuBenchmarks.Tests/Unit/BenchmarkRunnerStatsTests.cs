namespace GpuBenchmarks.Tests.Unit;

public class BenchmarkRunnerStatsTests
{
    [Fact]
    public void ComputeStats_ReturnsExpectedValues()
    {
        var (median, min, max, stddev) = BenchmarkRunner.ComputeStats(new[] { 1.0, 2.0, 3.0, 4.0 });

        Assert.Equal(2.5, median, 3);
        Assert.Equal(1.0, min, 3);
        Assert.Equal(4.0, max, 3);
        Assert.True(stddev > 1.0);
    }
}
