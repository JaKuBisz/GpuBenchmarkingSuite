using GpuBenchmarks.Cli;

namespace GpuBenchmarks.Tests.Unit;

public class VariantSelectionParserTests
{
    [Fact]
    public void Parse_ResolvesKnownTokens()
    {
        var result = VariantSelectionParser.Parse("seq,parallel_4,ilgpu,computesharp");

        Assert.NotNull(result);
        Assert.True(result!.IncludeSequential);
        Assert.Contains(4, result.ParallelThreadCounts);
        Assert.True(result.IncludeIlgpu);
        Assert.True(result.IncludeComputeSharp);
    }
}
