namespace GpuBenchmarks;

public class BenchmarkResult
{
    public string TaskName { get; set; } = string.Empty;
    public string Variant { get; set; } = string.Empty;
    public int InputSize { get; set; }
    public int RunNumber { get; set; }
    public double TimeMs { get; set; }
    public bool IsValid { get; set; }
}

