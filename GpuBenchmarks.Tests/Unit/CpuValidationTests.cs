using GpuBenchmarks.GameOfLife;
using GpuBenchmarks.MatrixMultiply;

namespace GpuBenchmarks.Tests.Unit;

public class CpuValidationTests
{
    [Fact]
    public void MatrixMultiply_ParallelMatchesSequential()
    {
        const int size = 32;
        var seq = new MatrixMultiplySequential();
        var par = new MatrixMultiplyParallel(4);

        seq.Setup(size);
        seq.Run();
        par.Setup(size);
        par.Run();

        Assert.True(par.Validate(seq));

        seq.Cleanup();
        par.Cleanup();
    }

    [Fact]
    public void GameOfLife_ParallelMatchesSequential()
    {
        const int size = 32;
        var seq = new GameOfLifeSequential();
        var par = new GameOfLifeParallel(4);

        seq.Setup(size);
        seq.Run();
        par.Setup(size);
        par.Run();

        Assert.True(par.Validate(seq));

        seq.Cleanup();
        par.Cleanup();
    }
}
