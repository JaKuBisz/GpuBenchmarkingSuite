using System.Globalization;
using System.Text;

namespace GpuBenchmarks.Reporting;

internal static class AnalysisWriter
{
    public static void WriteParallel1Analysis(
        List<(string task, string variant, int size, double median, double mean, double min, double max, double stddev, bool valid)> summaryRows,
        Dictionary<string, double> par1Medians,
        string resultsDir)
    {
        var csv = new StringBuilder();
        csv.AppendLine("TaskName,InputSize,Variant,MedianMs,SpeedupVsParallel1,OverheadPercentVsParallel1,IsValid");

        string Fmt(double v) => double.IsNaN(v) ? "N/A" : v.ToString("F4", CultureInfo.InvariantCulture);

        foreach (var (task, variant, size, median, _, _, _, _, valid) in summaryRows)
        {
            string key = $"{task}_{size}";
            if (!par1Medians.TryGetValue(key, out double par1Median) || par1Median <= 0)
            {
                continue;
            }

            double speedupVsParallel1 = 1.0;
            double overheadPercentVsParallel1 = 0.0;

            if (variant != "Parallel_1" && median > 0)
            {
                speedupVsParallel1 = par1Median / median;
                overheadPercentVsParallel1 = (median - par1Median) / par1Median * 100.0;
            }

            csv.AppendLine($"{task},{size},{variant},{Fmt(median)},{Fmt(speedupVsParallel1)},{Fmt(overheadPercentVsParallel1)},{valid}");
        }

        File.WriteAllText(Path.Combine(resultsDir, "parallel1_analysis.csv"), csv.ToString());
    }

    public static void WriteAmdahlAnalysis(
        List<(string task, string variant, int size, double median, double mean, double min, double max, double stddev, bool valid)> summaryRows,
        Dictionary<string, double> seqMedians,
        int[] threadCounts,
        string resultsDir)
    {
        const int fitThreads = 6;
        const string fitLabel = "Parallel_6";

        var csv = new StringBuilder();
        csv.AppendLine("TaskName,InputSize,EstimatedP,ThreadCount,TheoreticalSpeedup,ActualSpeedup,DeviationPercent,IsHyperthreading");

        string Fmt(double v) => double.IsNaN(v) ? "N/A" : v.ToString("F4", CultureInfo.InvariantCulture);

        var tasks = new[] { "MatrixMultiply", "GameOfLife" };
        foreach (var task in tasks)
        {
            var taskRows = summaryRows.Where(r => r.task == task).ToList();
            var sizes = taskRows.Select(r => r.size).Distinct().OrderBy(s => s).ToList();

            foreach (int size in sizes)
            {
                string seqKey = $"{task}_{size}";
                if (!seqMedians.TryGetValue(seqKey, out double seqMedian) || seqMedian <= 0)
                {
                    continue;
                }

                var fitRow = taskRows.FirstOrDefault(r => r.variant == fitLabel && r.size == size);
                if (fitRow == default)
                {
                    continue;
                }

                double fitSpeedup = seqMedian / fitRow.median;
                double estimatedP = double.NaN;
                if (fitThreads > 1 && fitSpeedup > 0)
                {
                    double denom = 1.0 / fitThreads - 1.0;
                    if (Math.Abs(denom) > 1e-10)
                    {
                        estimatedP = (1.0 / fitSpeedup - 1.0) / denom;
                    }

                    if (!double.IsNaN(estimatedP))
                    {
                        estimatedP = Math.Clamp(estimatedP, 0, 1);
                    }
                }

                foreach (int tc in threadCounts.Where(t => t > 1))
                {
                    double theoretical = double.NaN;
                    if (!double.IsNaN(estimatedP))
                    {
                        theoretical = 1.0 / ((1 - estimatedP) + estimatedP / tc);
                    }

                    var row = taskRows.FirstOrDefault(r => r.variant == $"Parallel_{tc}" && r.size == size);
                    double actual = (row != default && row.median > 0) ? seqMedian / row.median : double.NaN;

                    double deviation = double.NaN;
                    if (!double.IsNaN(theoretical) && !double.IsNaN(actual) && theoretical > 0)
                    {
                        deviation = Math.Abs(actual - theoretical) / theoretical * 100.0;
                    }

                    bool isHyperthreading = tc > fitThreads;
                    csv.AppendLine($"{task},{size},{Fmt(estimatedP)},{tc},{Fmt(theoretical)},{Fmt(actual)},{Fmt(deviation)},{isHyperthreading}");
                }
            }
        }

        File.WriteAllText(Path.Combine(resultsDir, "amdahl_analysis.csv"), csv.ToString());
    }
}
