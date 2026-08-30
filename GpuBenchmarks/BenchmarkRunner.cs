using System.Diagnostics;

namespace GpuBenchmarks;

public class BenchmarkRunner
{
    private const int WarmupRuns = 3;
    private const int DefaultMeasuredRuns = 10;

    /// <summary>
    /// Override warmup count (e.g. 1 for quick/debug mode).
    /// </summary>
    public int WarmupOverride { get; init; } = WarmupRuns;

    /// <summary>
    /// Override measured run count (e.g. 3 for quick/debug mode, or via --runs N).
    /// </summary>
    public int MeasuredRunsOverride { get; init; } = DefaultMeasuredRuns;

    public List<BenchmarkResult> Run(
        IBenchmark benchmark,
        IBenchmark? referenceForValidation,
        string taskName,
        string variant,
        int size)
    {
        RunWarmup(benchmark, size);
        double estimatedMs = MeasureProbeRun(benchmark, size);
        int measuredRuns = DetermineMeasuredRuns(estimatedMs);

        Console.WriteLine($"  Estimated single run: {estimatedMs:F1}ms, will do {measuredRuns} measured runs");

        // Setup reference once for validation
        if (referenceForValidation != null)
        {
            referenceForValidation.Setup(size);
            referenceForValidation.Run();
        }

        var results = new List<BenchmarkResult>();
        var sw = new Stopwatch();
        bool? validationResult = null;

        for (int i = 0; i < measuredRuns; i++)
        {
            benchmark.Setup(size);
            sw.Restart();
            benchmark.Run();
            sw.Stop();

            if (i == 0 && referenceForValidation != null)
            {
                try
                {
                    validationResult = benchmark.Validate(referenceForValidation);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [WARN] Validation threw exception: {ex.Message}");
                    validationResult = false;
                }
            }

            results.Add(new BenchmarkResult
            {
                TaskName = taskName,
                Variant = variant,
                InputSize = size,
                RunNumber = i + 1,
                TimeMs = sw.Elapsed.TotalMilliseconds,
                IsValid = validationResult ?? true
            });

            benchmark.Cleanup();
        }

        if (referenceForValidation != null)
            referenceForValidation.Cleanup();

        return results;
    }

    private void RunWarmup(IBenchmark benchmark, int size)
    {
        for (int i = 0; i < WarmupOverride; i++)
        {
            benchmark.Setup(size);
            benchmark.Run();
            benchmark.Cleanup();
        }
    }

    private static double MeasureProbeRun(IBenchmark benchmark, int size)
    {
        benchmark.Setup(size);
        var probe = Stopwatch.StartNew();
        benchmark.Run();
        probe.Stop();
        benchmark.Cleanup();
        return probe.Elapsed.TotalMilliseconds;
    }

    private int DetermineMeasuredRuns(double estimatedMs)
    {
        int measuredRuns = MeasuredRunsOverride;
        if (MeasuredRunsOverride != DefaultMeasuredRuns)
        {
            return measuredRuns;
        }

        if (estimatedMs > 60_000) return 3;
        if (estimatedMs > 30_000) return 5;
        return measuredRuns;
    }

    public static (double median, double min, double max, double stddev) ComputeStats(IEnumerable<double> times)
    {
        var sorted = times.OrderBy(x => x).ToList();
        int n = sorted.Count;
        double median = n % 2 == 0
            ? (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0
            : sorted[n / 2];
        double min = sorted.First();
        double max = sorted.Last();
        double mean = sorted.Average();
        double variance = sorted.Sum(t => (t - mean) * (t - mean)) / n;
        double stddev = Math.Sqrt(variance);
        return (median, min, max, stddev);
    }
}

