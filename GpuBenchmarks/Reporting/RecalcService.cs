using System.Globalization;
using System.Text;

namespace GpuBenchmarks.Reporting;

internal static class RecalcService
{
    public static void RecalcStrongFromRaw(string rawPath, string resultsDir)
    {
        var raw = LoadRawResultsFromCsv(rawPath);
        var summaryRows = ComputeSummaryRowsFromRaw(raw);

        var seqMedians = new Dictionary<string, double>();
        var par1Medians = new Dictionary<string, double>();
        foreach (var (task, variant, size, median, _, _, _, _, _) in summaryRows)
        {
            string key = $"{task}_{size}";
            if (variant == "Sequential") seqMedians[key] = median;
            if (variant == "Parallel_1") par1Medians[key] = median;
        }

        var summaryCsv = new StringBuilder();
        summaryCsv.AppendLine("TaskName,Variant,InputSize,MedianMs,MeanMs,MinMs,MaxMs,StdDevMs,CvPercent,SpeedupVsSequential,SpeedupVsParallel1,IsValid");

        foreach (var (task, variant, size, median, mean, min, max, stddev, valid) in summaryRows.OrderBy(r => r.task).ThenBy(r => r.size).ThenBy(r => r.variant))
        {
            string key = $"{task}_{size}";
            double speedupVsSequential = 1.0;
            double speedupVsParallel1 = 1.0;

            if (variant != "Sequential" && seqMedians.TryGetValue(key, out double seqMed) && median > 0)
            {
                speedupVsSequential = seqMed / median;
            }

            if (variant != "Parallel_1" && par1Medians.TryGetValue(key, out double par1Med) && median > 0)
            {
                speedupVsParallel1 = par1Med / median;
            }

            double cvPercent = mean > 0 ? (stddev / mean * 100.0) : double.NaN;
            string cvStr = double.IsNaN(cvPercent) ? "N/A" : cvPercent.ToString("F2", CultureInfo.InvariantCulture);

            summaryCsv.AppendLine(
                $"{task},{variant},{size}," +
                $"{median.ToString("F4", CultureInfo.InvariantCulture)}," +
                $"{mean.ToString("F4", CultureInfo.InvariantCulture)}," +
                $"{min.ToString("F4", CultureInfo.InvariantCulture)}," +
                $"{max.ToString("F4", CultureInfo.InvariantCulture)}," +
                $"{stddev.ToString("F4", CultureInfo.InvariantCulture)}," +
                $"{cvStr}," +
                $"{speedupVsSequential.ToString("F4", CultureInfo.InvariantCulture)}," +
                $"{speedupVsParallel1.ToString("F4", CultureInfo.InvariantCulture)}," +
                $"{valid}");
        }

        File.WriteAllText(Path.Combine(resultsDir, "summary.csv"), summaryCsv.ToString());
        AnalysisWriter.WriteParallel1Analysis(summaryRows, par1Medians, resultsDir);
        AnalysisWriter.WriteAmdahlAnalysis(summaryRows, seqMedians, ParseThreadCountsFromSummary(summaryRows), resultsDir);
    }

    public static void RecalcWeakFromRaw(string rawPath, string resultsDir)
    {
        var raw = LoadRawResultsFromCsv(rawPath);
        var summaryRows = ComputeSummaryRowsFromRaw(raw);

        var baseSizeByTask = summaryRows.Where(r => r.variant == "Sequential").GroupBy(r => r.task).ToDictionary(g => g.Key, g => g.Min(x => x.size));
        var baseMedianByTask = new Dictionary<string, double>();
        foreach (var task in baseSizeByTask.Keys)
        {
            int baseSize = baseSizeByTask[task];
            var baseRow = summaryRows.First(r => r.task == task && r.variant == "Sequential" && r.size == baseSize);
            baseMedianByTask[task] = baseRow.median;
        }

        var weakSummary = new StringBuilder();
        weakSummary.AppendLine("TaskName,Variant,InputSize,MedianMs,MeanMs,MinMs,MaxMs,StdDevMs,CvPercent,SpeedupVsBase,IsValid");
        foreach (var (task, variant, size, median, mean, min, max, stddev, valid) in summaryRows.OrderBy(r => r.task).ThenBy(r => r.size).ThenBy(r => r.variant))
        {
            double speedupVsBase = double.NaN;
            if (baseMedianByTask.TryGetValue(task, out double baseMed) && median > 0)
            {
                speedupVsBase = baseMed / median;
            }

            double cvPercent = mean > 0 ? (stddev / mean * 100.0) : double.NaN;
            string cvStr = double.IsNaN(cvPercent) ? "N/A" : cvPercent.ToString("F2", CultureInfo.InvariantCulture);
            weakSummary.AppendLine($"{task},{variant},{size},{median:F4},{mean:F4},{min:F4},{max:F4},{stddev:F4},{cvStr},{(double.IsNaN(speedupVsBase) ? "N/A" : speedupVsBase.ToString("F4", CultureInfo.InvariantCulture))},{valid}");
        }

        File.WriteAllText(Path.Combine(resultsDir, "weak_summary.csv"), weakSummary.ToString());
        WriteWeakGustafsonAnalysis(summaryRows, baseSizeByTask, baseMedianByTask, resultsDir);
    }

    private static void WriteWeakGustafsonAnalysis(
        List<(string task, string variant, int size, double median, double mean, double min, double max, double stddev, bool valid)> summaryRows,
        Dictionary<string, int> baseSizeByTask,
        Dictionary<string, double> baseMedianByTask,
        string resultsDir)
    {
        int[] threadCounts = ParseThreadCountsFromSummary(summaryRows);
        string Fmt(double v, string fmt) => double.IsNaN(v) ? "N/A" : v.ToString(fmt, CultureInfo.InvariantCulture);
        var gust = new StringBuilder();
        gust.AppendLine("TaskName,BaseSize,ThreadCount,ScaledSize,ParallelMedianMs,BaseSeqMedianMs,ApproxSpeedupVsBaseSeq,ApproxAlphaFromBaseSeq,SeqScaledMedianMs,GustafsonSpeedupStrict,AlphaStrict,IsValid");

        foreach (var task in baseSizeByTask.Keys.OrderBy(x => x))
        {
            int baseSize = baseSizeByTask[task];
            double baseMed = baseMedianByTask[task];
            foreach (int tc in threadCounts.Where(t => t > 1))
            {
                var parRow = summaryRows.FirstOrDefault(r => r.task == task && r.variant == $"Parallel_{tc}");
                if (parRow == default || parRow.median <= 0) continue;

                int scaledSize = parRow.size;
                double approxS = baseMed / parRow.median;
                double approxAlpha = (tc - approxS) / (tc - 1);
                var seqScaledRow = summaryRows.FirstOrDefault(r => r.task == task && r.variant == "Sequential" && r.size == scaledSize);
                double seqScaledMed = seqScaledRow == default ? double.NaN : seqScaledRow.median;
                double strictS = !double.IsNaN(seqScaledMed) && seqScaledMed > 0 ? seqScaledMed / parRow.median : double.NaN;
                double strictAlpha = !double.IsNaN(strictS) ? (tc - strictS) / (tc - 1) : double.NaN;

                gust.AppendLine($"{task},{baseSize},{tc},{scaledSize},{parRow.median:F4},{baseMed:F4},{Fmt(approxS, "F4")},{Fmt(approxAlpha, "F6")},{Fmt(seqScaledMed, "F4")},{Fmt(strictS, "F4")},{Fmt(strictAlpha, "F6")},{parRow.valid}");
            }
        }

        File.WriteAllText(Path.Combine(resultsDir, "weak_gustafson_analysis.csv"), gust.ToString());
    }

    private static List<BenchmarkResult> LoadRawResultsFromCsv(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Raw results not found: {path}");
        var results = new List<BenchmarkResult>();
        bool isFirst = true;
        foreach (var line in File.ReadLines(path))
        {
            if (isFirst) { isFirst = false; continue; }
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(',');
            if (parts.Length < 6) continue;
            results.Add(new BenchmarkResult
            {
                TaskName = parts[0],
                Variant = parts[1],
                InputSize = int.Parse(parts[2], CultureInfo.InvariantCulture),
                RunNumber = int.Parse(parts[3], CultureInfo.InvariantCulture),
                TimeMs = double.Parse(parts[4], CultureInfo.InvariantCulture),
                IsValid = bool.Parse(parts[5])
            });
        }
        return results;
    }

    private static List<(string task, string variant, int size, double median, double mean, double min, double max, double stddev, bool valid)> ComputeSummaryRowsFromRaw(List<BenchmarkResult> raw)
    {
        var summary = new List<(string, string, int, double, double, double, double, double, bool)>();
        foreach (var g in raw.GroupBy(r => (r.TaskName, r.Variant, r.InputSize)))
        {
            var times = g.Select(x => x.TimeMs);
            var (median, min, max, stddev) = BenchmarkRunner.ComputeStats(times);
            double mean = g.Average(x => x.TimeMs);
            bool allValid = g.All(x => x.IsValid);
            summary.Add((g.Key.TaskName, g.Key.Variant, g.Key.InputSize, median, mean, min, max, stddev, allValid));
        }
        return summary;
    }

    private static int[] ParseThreadCountsFromSummary(IEnumerable<(string task, string variant, int size, double median, double mean, double min, double max, double stddev, bool valid)> rows)
    {
        var tcs = new HashSet<int>();
        foreach (var r in rows)
        {
            if (!r.variant.StartsWith("Parallel_", StringComparison.OrdinalIgnoreCase)) continue;
            string suffix = r.variant["Parallel_".Length..];
            if (int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tc) && tc > 0)
            {
                tcs.Add(tc);
            }
        }
        return tcs.OrderBy(x => x).ToArray();
    }
}
