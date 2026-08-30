using System.Globalization;

namespace GpuBenchmarks.Cli;

internal sealed class VariantSelection
{
    public bool IncludeSequential { get; set; }
    public bool IncludeParallelAll { get; set; }
    public HashSet<int> ParallelThreadCounts { get; } = new();
    public bool IncludeIlgpu { get; set; }
    public bool IncludeComputeSharp { get; set; }

    public override string ToString()
    {
        var parts = new List<string>();

        if (IncludeSequential)
        {
            parts.Add("Sequential");
        }

        if (IncludeParallelAll)
        {
            parts.Add("Parallel(all)");
        }

        foreach (int threadCount in ParallelThreadCounts.OrderBy(x => x))
        {
            parts.Add($"Parallel_{threadCount}");
        }

        if (IncludeIlgpu)
        {
            parts.Add("ILGPU");
        }

        if (IncludeComputeSharp)
        {
            parts.Add("ComputeSharp");
        }

        return parts.Count == 0 ? "(none)" : string.Join(", ", parts);
    }
}

internal static class VariantSelectionParser
{
    public static VariantSelection? Parse(string rawValue)
    {
        var selection = new VariantSelection();
        bool sawValidToken = false;

        foreach (var token in rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string normalized = token.Trim();
            if (normalized.Length == 0)
            {
                continue;
            }

            if (TryAddVariantToken(selection, normalized))
            {
                sawValidToken = true;
                continue;
            }

            Console.WriteLine($"[WARN] Ignoring unknown variant token: {normalized}");
        }

        if (!sawValidToken)
        {
            Console.WriteLine($"[WARN] Ignoring invalid value for --variants: {rawValue}");
            return null;
        }

        return selection;
    }

    public static bool ShouldRunSequential(VariantSelection? selection) =>
        selection is null || selection.IncludeSequential;

    public static bool ShouldRunParallel(VariantSelection? selection, int threadCount) =>
        selection is null || selection.IncludeParallelAll || selection.ParallelThreadCounts.Contains(threadCount);

    public static bool ShouldRunIlgpu(VariantSelection? selection) =>
        selection is null || selection.IncludeIlgpu;

    public static bool ShouldRunComputeSharp(VariantSelection? selection) =>
        selection is null || selection.IncludeComputeSharp;

    private static bool TryAddVariantToken(VariantSelection selection, string token)
    {
        if (token.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            selection.IncludeSequential = true;
            selection.IncludeParallelAll = true;
            selection.IncludeIlgpu = true;
            selection.IncludeComputeSharp = true;
            return true;
        }

        if (token.Equals("seq", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("sequential", StringComparison.OrdinalIgnoreCase))
        {
            selection.IncludeSequential = true;
            return true;
        }

        if (token.Equals("parallel", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("par", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("threads", StringComparison.OrdinalIgnoreCase))
        {
            selection.IncludeParallelAll = true;
            return true;
        }

        if (TryParseParallelThreadCount(token, out int threadCount))
        {
            selection.ParallelThreadCounts.Add(threadCount);
            return true;
        }

        if (token.Equals("ilgpu", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("gpu", StringComparison.OrdinalIgnoreCase))
        {
            selection.IncludeIlgpu = true;
            return true;
        }

        if (token.Equals("computesharp", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("gpu_computesharp", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("cs", StringComparison.OrdinalIgnoreCase))
        {
            selection.IncludeComputeSharp = true;
            return true;
        }

        return false;
    }

    private static bool TryParseParallelThreadCount(string token, out int threadCount)
    {
        threadCount = 0;

        if (!token.StartsWith("parallel", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string suffix = token["parallel".Length..].Trim();
        if (suffix.Length == 0)
        {
            return false;
        }

        if (suffix[0] is '_' or '-' or ':')
        {
            suffix = suffix[1..].Trim();
        }

        return int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out threadCount) && threadCount > 0;
    }
}
