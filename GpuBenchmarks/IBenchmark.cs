namespace GpuBenchmarks;

public interface IBenchmark
{
    string Name { get; }
    void Setup(int size);
    void Run();
    bool Validate(IBenchmark reference);
    void Cleanup();
    float[] GetResult();
}

