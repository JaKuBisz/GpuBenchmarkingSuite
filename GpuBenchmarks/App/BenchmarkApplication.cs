using System.Globalization;
using System.Text;
using ComputeSharp;
using GpuBenchmarks.Cli;
using GpuBenchmarks.GameOfLife;
using GpuBenchmarks.MatrixMultiply;
using GpuBenchmarks.Reporting;
using GpuBenchmarks.Scaling;
using GpuBenchmarks.SystemInfo;

namespace GpuBenchmarks.App;

internal sealed class BenchmarkApplication
{
    private const string ResultsDir = "results";

    public int Run(string[] args)
    {
        Directory.CreateDirectory(ResultsDir);

        if (args.Contains("--list-devices"))
        {
            PrintDevices();
            return 0;
        }

        bool recalcStrong = args.Contains("--recalc-strong");
        bool recalcWeak = args.Contains("--recalc-weak");
        if (args.Contains("--recalc"))
        {
            recalcStrong = true;
            recalcWeak = true;
        }

        if (recalcStrong || recalcWeak)
        {
            RunRecalc(recalcStrong, recalcWeak);
            return 0;
        }

        Console.WriteLine("=== GPU Benchmark Suite ===");
        Console.WriteLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine();

        var sysInfo = SystemInfoCollector.Collect();
        File.WriteAllText(Path.Combine(ResultsDir, "system_info.txt"), sysInfo);
        Console.WriteLine(sysInfo);

        int? selectedDeviceIndex = GpuSelector.SelectDevice(args);
        bool gpuAvailable = selectedDeviceIndex.HasValue;
        bool computeSharpAvailable = MatrixMultiplyComputeSharp.IsAvailable;

        bool quickMode = args.Contains("--quick");
        int? runsOverride = CliArgumentParser.TryGetPositiveIntArg(args, "--runs");
        int[]? customMatrixSizes = CliArgumentParser.TryGetPositiveIntListArg(args, "--sizes");
        int[]? customGolSizes = CliArgumentParser.TryGetPositiveIntListArg(args, "--gol-sizes");
        string? variantsArg = CliArgumentParser.TryGetStringArg(args, "--variants");
        VariantSelection? variantSelection = variantsArg is null ? null : VariantSelectionParser.Parse(variantsArg);

        int[] matrixSizes = customMatrixSizes ?? (quickMode ? new[] { 64, 128, 256 } : new[] { 64, 128, 256, 512, 1024, 2048, 4096 });
        int[] golSizes = customGolSizes ?? (quickMode ? new[] { 8, 16, 24, 32, 64, 100 } : new[] { 8, 12, 16, 24, 32, 64, 100, 150, 250, 500, 1000, 2000, 5000, 10000 });
        int maxThreads = Environment.ProcessorCount;
        int[] threadCounts = quickMode
            ? new[] { 1, 4, 6, maxThreads }.Where(t => t <= maxThreads).Distinct().ToArray()
            : new[] { 1, 2, 4, 6, 8, 12 }.Where(t => t <= maxThreads).Distinct().ToArray();

        bool weakScaleMode = args.Contains("--weak-scale");
        bool weakGustafsonStrict = args.Contains("--weak-gustafson-strict");
        int weakBaseSize = CliArgumentParser.TryGetPositiveIntArg(args, "--weak-base") ?? 256;

        var runner = new BenchmarkRunner
        {
            WarmupOverride = quickMode ? 1 : 3,
            MeasuredRunsOverride = runsOverride ?? (quickMode ? 3 : 10)
        };

        string ilgpuVariantLabel = gpuAvailable
            ? $"ILGPU_{GpuSelector.GetAvailableDevices()[selectedDeviceIndex!.Value].Type}"
            : "ILGPU";

        PrintRunConfiguration(
            quickMode,
            runsOverride,
            customMatrixSizes,
            customGolSizes,
            variantSelection,
            threadCounts,
            gpuAvailable,
            selectedDeviceIndex,
            computeSharpAvailable,
            weakScaleMode,
            weakBaseSize,
            weakGustafsonStrict);

        if (args.Contains("--ilgpu-scaling"))
        {
            int[] ilgpuEptList = (CliArgumentParser.TryGetPositiveIntListArg(args, "--ilgpu-ept")
                                  ?? (quickMode ? new[] { 16, 8, 4, 2, 1 } : new[] { 64, 32, 16, 8, 4, 2, 1 }))
                .Distinct()
                .OrderByDescending(x => x)
                .ToArray();

            RunIlgpuScaling(runner, variantSelection, gpuAvailable, selectedDeviceIndex, weakScaleMode, weakBaseSize, threadCounts, matrixSizes, golSizes, ilgpuEptList, ilgpuVariantLabel);
            return 0;
        }

        if (weakScaleMode)
        {
            RunWeakScaling(runner, variantSelection, weakBaseSize, threadCounts, weakGustafsonStrict);
            return 0;
        }

        RunStrongScaling(runner, variantSelection, matrixSizes, golSizes, threadCounts, gpuAvailable, computeSharpAvailable, selectedDeviceIndex, ilgpuVariantLabel);
        return 0;
    }

    private static void RunRecalc(bool recalcStrong, bool recalcWeak)
    {
        if (recalcStrong)
        {
            RecalcService.RecalcStrongFromRaw(Path.Combine(ResultsDir, "raw_results.csv"), ResultsDir);
        }
        if (recalcWeak)
        {
            RecalcService.RecalcWeakFromRaw(Path.Combine(ResultsDir, "weak_raw_results.csv"), ResultsDir);
        }
    }

    private static void RunStrongScaling(
        BenchmarkRunner runner,
        VariantSelection? variantSelection,
        int[] matrixSizes,
        int[] golSizes,
        int[] threadCounts,
        bool gpuAvailable,
        bool computeSharpAvailable,
        int? selectedDeviceIndex,
        string ilgpuVariantLabel)
    {
        Console.WriteLine("=== MATRIX MULTIPLICATION ===");
        var rawCsv = new StringBuilder();
        rawCsv.AppendLine("TaskName,Variant,InputSize,RunNumber,TimeMs,IsValid");
        var summaryRows = new List<(string task, string variant, int size, double median, double mean, double min, double max, double stddev, bool valid)>();
        var seqMedians = new Dictionary<string, double>();
        var par1Medians = new Dictionary<string, double>();

        void RecordAndPrint(string task, string variant, int size, List<BenchmarkResult> results)
        {
            foreach (var r in results)
            {
                rawCsv.AppendLine($"{r.TaskName},{r.Variant},{r.InputSize},{r.RunNumber},{r.TimeMs.ToString("F4", CultureInfo.InvariantCulture)},{r.IsValid}");
            }
            var (median, min, max, stddev) = BenchmarkRunner.ComputeStats(results.Select(r => r.TimeMs));
            double mean = results.Average(r => r.TimeMs);
            bool allValid = results.All(r => r.IsValid);
            summaryRows.Add((task, variant, size, median, mean, min, max, stddev, allValid));
            string key = $"{task}_{size}";
            if (variant == "Sequential") seqMedians[key] = median;
            if (variant == "Parallel_1") par1Medians[key] = median;
        }

        void RunConfig(IBenchmark bench, IBenchmark? reference, string task, string variant, int size)
        {
            Console.Write($"  [{task}] {variant}, size={size}: ");
            try
            {
                var results = runner.Run(bench, reference, task, variant, size);
                RecordAndPrint(task, variant, size, results);
                var median = summaryRows.Last().median;
                bool valid = summaryRows.Last().valid;
                Console.WriteLine($"median={median:F2}ms, valid={valid}");
            }
            catch (OutOfMemoryException)
            {
                Console.WriteLine("SKIPPED (OutOfMemoryException)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
            }
        }

        foreach (int size in matrixSizes)
        {
            Console.WriteLine($"\n--- Size {size}x{size} ---");
            if (VariantSelectionParser.ShouldRunSequential(variantSelection))
                RunConfig(new MatrixMultiplySequential(), null, "MatrixMultiply", "Sequential", size);

            MatrixMultiplySequential? refMat = null;
            if (size <= 1024)
            {
                refMat = new MatrixMultiplySequential();
                refMat.Setup(size);
                refMat.Run();
            }

            foreach (int tc in threadCounts)
            {
                if (VariantSelectionParser.ShouldRunParallel(variantSelection, tc))
                    RunConfig(new MatrixMultiplyParallel(tc), refMat, "MatrixMultiply", $"Parallel_{tc}", size);
            }

            if (VariantSelectionParser.ShouldRunIlgpu(variantSelection) && gpuAvailable)
                RunConfig(new MatrixMultiplyGpu(selectedDeviceIndex!.Value), refMat, "MatrixMultiply", ilgpuVariantLabel, size);

            if (VariantSelectionParser.ShouldRunComputeSharp(variantSelection) && computeSharpAvailable)
                RunConfig(new MatrixMultiplyComputeSharp(), refMat, "MatrixMultiply", "GPU_ComputeSharp", size);

            refMat?.Cleanup();
        }

        foreach (int size in golSizes)
        {
            if (size == golSizes[0])
            {
                Console.WriteLine("\n=== GAME OF LIFE ===");
            }

            Console.WriteLine($"\n--- Grid {size}x{size} ---");
            if (VariantSelectionParser.ShouldRunSequential(variantSelection))
                RunConfig(new GameOfLifeSequential(), null, "GameOfLife", "Sequential", size);

            GameOfLifeSequential? refGol = null;
            if (size <= 2000)
            {
                refGol = new GameOfLifeSequential();
                refGol.Setup(size);
                refGol.Run();
            }

            foreach (int tc in threadCounts)
            {
                if (VariantSelectionParser.ShouldRunParallel(variantSelection, tc))
                    RunConfig(new GameOfLifeParallel(tc), refGol, "GameOfLife", $"Parallel_{tc}", size);
            }

            if (VariantSelectionParser.ShouldRunIlgpu(variantSelection) && gpuAvailable)
                RunConfig(new GameOfLifeGpu(selectedDeviceIndex!.Value), refGol, "GameOfLife", ilgpuVariantLabel, size);

            if (VariantSelectionParser.ShouldRunComputeSharp(variantSelection) && computeSharpAvailable)
                RunConfig(new GameOfLifeComputeSharp(), refGol, "GameOfLife", "GPU_ComputeSharp", size);

            refGol?.Cleanup();
        }

        File.WriteAllText(Path.Combine(ResultsDir, "raw_results.csv"), rawCsv.ToString());
        WriteStrongSummary(summaryRows, seqMedians, par1Medians);
        AnalysisWriter.WriteParallel1Analysis(summaryRows, par1Medians, ResultsDir);
        AnalysisWriter.WriteAmdahlAnalysis(summaryRows, seqMedians, threadCounts, ResultsDir);
        Console.WriteLine($"\n=== All benchmarks complete! ===\nResults in: {Path.GetFullPath(ResultsDir)}");
    }

    private static void WriteStrongSummary(
        List<(string task, string variant, int size, double median, double mean, double min, double max, double stddev, bool valid)> summaryRows,
        Dictionary<string, double> seqMedians,
        Dictionary<string, double> par1Medians)
    {
        var summaryCsv = new StringBuilder();
        summaryCsv.AppendLine("TaskName,Variant,InputSize,MedianMs,MeanMs,MinMs,MaxMs,StdDevMs,CvPercent,SpeedupVsSequential,SpeedupVsParallel1,IsValid");
        foreach (var (task, variant, size, median, mean, min, max, stddev, valid) in summaryRows)
        {
            double speedupVsSequential = variant == "Sequential" ? 1.0 : double.NaN;
            double speedupVsParallel1 = variant == "Parallel_1" ? 1.0 : double.NaN;
            string seqKey = $"{task}_{size}";
            if (variant != "Sequential" && seqMedians.TryGetValue(seqKey, out double seqMed) && median > 0)
                speedupVsSequential = seqMed / median;
            if (variant != "Parallel_1" && par1Medians.TryGetValue(seqKey, out double par1Med) && median > 0)
                speedupVsParallel1 = par1Med / median;
            double cvPercent = mean > 0 ? (stddev / mean * 100.0) : double.NaN;
            summaryCsv.AppendLine($"{task},{variant},{size},{median:F4},{mean:F4},{min:F4},{max:F4},{stddev:F4},{(double.IsNaN(cvPercent) ? "N/A" : cvPercent.ToString("F2", CultureInfo.InvariantCulture))},{speedupVsSequential:F4},{speedupVsParallel1:F4},{valid}");
        }
        File.WriteAllText(Path.Combine(ResultsDir, "summary.csv"), summaryCsv.ToString());
    }

    private static void RunWeakScaling(BenchmarkRunner runner, VariantSelection? variantSelection, int weakBaseSize, int[] threadCounts, bool weakGustafsonStrict)
    {
        Console.WriteLine("=== WEAK-SCALING RUN ===");
        var rawWeak = new StringBuilder();
        rawWeak.AppendLine("TaskName,Variant,InputSize,RunNumber,TimeMs,IsValid");
        var summaryWeak = new List<(string task, string variant, int size, double median, double mean, double min, double max, double stddev, bool valid)>();
        var seqMedians = new Dictionary<string, double>();

        void RunConfigWeak(IBenchmark bench, string task, string variant, int size)
        {
            Console.Write($"  [WEAK] [{task}] {variant}, size={size}: ");
            try
            {
                var results = runner.Run(bench, null, task, variant, size);
                foreach (var r in results)
                    rawWeak.AppendLine($"{r.TaskName},{r.Variant},{r.InputSize},{r.RunNumber},{r.TimeMs.ToString("F4", CultureInfo.InvariantCulture)},{r.IsValid}");
                var (median, min, max, stddev) = BenchmarkRunner.ComputeStats(results.Select(r => r.TimeMs));
                double mean = results.Average(r => r.TimeMs);
                bool allValid = results.All(r => r.IsValid);
                summaryWeak.Add((task, variant, size, median, mean, min, max, stddev, allValid));
                if (variant == "Sequential") seqMedians[$"{task}_{size}"] = median;
                Console.WriteLine($"median={median:F2}ms, valid={allValid}");
            }
            catch (OutOfMemoryException)
            {
                Console.WriteLine("SKIPPED (OutOfMemoryException)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
            }
        }

        foreach (var task in new[] { "MatrixMultiply", "GameOfLife" })
        {
            Console.WriteLine($"\n--- {task} (weak-scaling) ---");
            if (VariantSelectionParser.ShouldRunSequential(variantSelection))
            {
                RunConfigWeak(task == "MatrixMultiply" ? new MatrixMultiplySequential() : new GameOfLifeSequential(), task, "Sequential", weakBaseSize);
            }

            foreach (int tc in threadCounts)
            {
                if (!VariantSelectionParser.ShouldRunParallel(variantSelection, tc))
                    continue;
                int scaled = WeakScalingMath.ComputeScaledSize(weakBaseSize, tc, task);
                RunConfigWeak(task == "MatrixMultiply" ? new MatrixMultiplyParallel(tc) : new GameOfLifeParallel(tc), task, $"Parallel_{tc}", scaled);
            }
        }

        if (weakGustafsonStrict && VariantSelectionParser.ShouldRunSequential(variantSelection))
        {
            foreach (var task in new[] { "MatrixMultiply", "GameOfLife" })
            {
                foreach (int tc in threadCounts.Where(t => t > 1))
                {
                    if (!VariantSelectionParser.ShouldRunParallel(variantSelection, tc))
                        continue;
                    int scaled = WeakScalingMath.ComputeScaledSize(weakBaseSize, tc, task);
                    if (!seqMedians.ContainsKey($"{task}_{scaled}"))
                        RunConfigWeak(task == "MatrixMultiply" ? new MatrixMultiplySequential() : new GameOfLifeSequential(), task, "Sequential", scaled);
                }
            }
        }

        File.WriteAllText(Path.Combine(ResultsDir, "weak_raw_results.csv"), rawWeak.ToString());
        WriteWeakSummary(summaryWeak, seqMedians, weakBaseSize);
        WriteWeakGustafson(summaryWeak, seqMedians, weakBaseSize, threadCounts);
        Console.WriteLine($"\n=== Weak-scaling run complete ===\nResults in: {Path.GetFullPath(ResultsDir)}");
    }

    private static void WriteWeakSummary(
        List<(string task, string variant, int size, double median, double mean, double min, double max, double stddev, bool valid)> summaryWeak,
        Dictionary<string, double> seqMedians,
        int weakBaseSize)
    {
        var summaryWeakCsv = new StringBuilder();
        summaryWeakCsv.AppendLine("TaskName,Variant,InputSize,MedianMs,MeanMs,MinMs,MaxMs,StdDevMs,CvPercent,SpeedupVsBase,IsValid");
        foreach (var (task, variant, size, median, mean, min, max, stddev, valid) in summaryWeak)
        {
            double speedupVsBase = double.NaN;
            if (seqMedians.TryGetValue($"{task}_{weakBaseSize}", out double baseMed) && median > 0)
                speedupVsBase = baseMed / median;
            double cvPercent = mean > 0 ? (stddev / mean * 100.0) : double.NaN;
            summaryWeakCsv.AppendLine($"{task},{variant},{size},{median:F4},{mean:F4},{min:F4},{max:F4},{stddev:F4},{(double.IsNaN(cvPercent) ? "N/A" : cvPercent.ToString("F2", CultureInfo.InvariantCulture))},{(double.IsNaN(speedupVsBase) ? "N/A" : speedupVsBase.ToString("F4", CultureInfo.InvariantCulture))},{valid}");
        }
        File.WriteAllText(Path.Combine(ResultsDir, "weak_summary.csv"), summaryWeakCsv.ToString());
    }

    private static void WriteWeakGustafson(
        List<(string task, string variant, int size, double median, double mean, double min, double max, double stddev, bool valid)> summaryWeak,
        Dictionary<string, double> seqMedians,
        int weakBaseSize,
        int[] threadCounts)
    {
        var gustCsv = new StringBuilder();
        gustCsv.AppendLine("TaskName,BaseSize,ThreadCount,ScaledSize,ParallelMedianMs,BaseSeqMedianMs,ApproxSpeedupVsBaseSeq,ApproxAlphaFromBaseSeq,SeqScaledMedianMs,GustafsonSpeedupStrict,AlphaStrict,IsValid");
        foreach (var task in new[] { "MatrixMultiply", "GameOfLife" })
        {
            foreach (int tc in threadCounts.Where(t => t > 1))
            {
                int scaled = WeakScalingMath.ComputeScaledSize(weakBaseSize, tc, task);
                var row = summaryWeak.FirstOrDefault(r => r.task == task && r.variant == $"Parallel_{tc}" && r.size == scaled);
                if (row == default || !seqMedians.TryGetValue($"{task}_{weakBaseSize}", out double baseMed) || row.median <= 0) continue;
                double approxS = baseMed / row.median;
                double approxAlpha = (tc - approxS) / (tc - 1);
                double seqScaledMedian = seqMedians.TryGetValue($"{task}_{scaled}", out var strictSeq) ? strictSeq : double.NaN;
                double strictS = (!double.IsNaN(seqScaledMedian) && seqScaledMedian > 0) ? seqScaledMedian / row.median : double.NaN;
                double strictAlpha = (!double.IsNaN(strictS)) ? (tc - strictS) / (tc - 1) : double.NaN;
                string Fmt(double value, string fmt) => double.IsNaN(value) ? "N/A" : value.ToString(fmt, CultureInfo.InvariantCulture);
                gustCsv.AppendLine($"{task},{weakBaseSize},{tc},{scaled},{row.median:F4},{baseMed:F4},{Fmt(approxS, "F4")},{Fmt(approxAlpha, "F6")},{Fmt(seqScaledMedian, "F4")},{Fmt(strictS, "F4")},{Fmt(strictAlpha, "F6")},{row.valid}");
            }
        }
        File.WriteAllText(Path.Combine(ResultsDir, "weak_gustafson_analysis.csv"), gustCsv.ToString());
    }

    private static void RunIlgpuScaling(
        BenchmarkRunner runner,
        VariantSelection? variantSelection,
        bool gpuAvailable,
        int? selectedDeviceIndex,
        bool weakScaleMode,
        int weakBaseSize,
        int[] threadCounts,
        int[] matrixSizes,
        int[] golSizes,
        int[] ilgpuEptList,
        string ilgpuVariantLabel)
    {
        if (variantSelection != null && !variantSelection.IncludeIlgpu)
        {
            Console.WriteLine("[INFO] --ilgpu-scaling skipped because selected variants do not include ILGPU.");
            return;
        }
        if (!gpuAvailable)
        {
            Console.WriteLine("[WARN] --ilgpu-scaling requires GPU accelerator. None detected.");
            return;
        }

        int baselineEpt = ilgpuEptList.Max();
        string rawFile = weakScaleMode ? "weak_ilgpu_raw_results.csv" : "ilgpu_raw_results.csv";
        string summaryFile = weakScaleMode ? "weak_ilgpu_summary.csv" : "ilgpu_summary.csv";
        string analysisFile = weakScaleMode ? "weak_ilgpu_scaling_analysis.csv" : "ilgpu_scaling_analysis.csv";

        var raw = new StringBuilder();
        raw.AppendLine("TaskName,Variant,InputSize,RunNumber,TimeMs,IsValid");
        var summary = new List<(string task, int size, int ept, int logicalThreads, double median, double mean, double min, double max, double stddev, bool valid, int threadCount)>();

        Console.WriteLine("ILGPU SCALING MODE ENABLED");
        Console.WriteLine($"  Ept sweep: [{string.Join(", ", ilgpuEptList)}], baseline: {baselineEpt}");
        Console.WriteLine($"  Mode: {(weakScaleMode ? "WEAK" : "STRONG")}");

        void RunIlgpuConfig(string task, int size, int ept, int threadCount)
        {
            int total = checked(size * size);
            int logicalThreads = (total + ept - 1) / ept;
            string variant = $"{ilgpuVariantLabel}_Ept{ept}";
            IBenchmark bench = task == "MatrixMultiply"
                ? new MatrixMultiplyGpu(selectedDeviceIndex!.Value, ept)
                : new GameOfLifeGpu(selectedDeviceIndex!.Value, ept);
            IBenchmark? reference = task switch
            {
                "MatrixMultiply" when size <= 1024 => new MatrixMultiplySequential(),
                "GameOfLife" when size <= 2000 => new GameOfLifeSequential(),
                _ => null
            };

            Console.Write($"  [ILGPU-SCALE] [{task}] {variant}, size={size} (threads={logicalThreads}): ");
            try
            {
                var results = runner.Run(bench, reference, task, variant, size);
                foreach (var r in results)
                    raw.AppendLine($"{r.TaskName},{r.Variant},{r.InputSize},{r.RunNumber},{r.TimeMs.ToString("F4", CultureInfo.InvariantCulture)},{r.IsValid}");
                var (median, min, max, stddev) = BenchmarkRunner.ComputeStats(results.Select(r => r.TimeMs));
                double mean = results.Average(r => r.TimeMs);
                bool valid = results.All(r => r.IsValid);
                summary.Add((task, size, ept, logicalThreads, median, mean, min, max, stddev, valid, threadCount));
                Console.WriteLine($"median={median:F2}ms, valid={valid}");
            }
            catch (OutOfMemoryException)
            {
                Console.WriteLine("SKIPPED (OutOfMemoryException)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
            }
        }

        if (!weakScaleMode)
        {
            Console.WriteLine("=== ILGPU STRONG-SCALING SWEEP ===");
            foreach (var (task, size) in matrixSizes.Select(size => ("MatrixMultiply", size)).Concat(golSizes.Select(size => ("GameOfLife", size))))
            {
                foreach (int ept in ilgpuEptList)
                {
                    RunIlgpuConfig(task, size, ept, threadCount: 0);
                }
            }
        }
        else
        {
            Console.WriteLine("=== ILGPU WEAK-SCALING SWEEP ===");
            foreach (var task in new[] { "MatrixMultiply", "GameOfLife" })
            {
                foreach (int tc in threadCounts)
                {
                    int size = WeakScalingMath.ComputeScaledSize(weakBaseSize, tc, task);
                    foreach (int ept in ilgpuEptList)
                    {
                        RunIlgpuConfig(task, size, ept, tc);
                    }
                }
            }
        }

        File.WriteAllText(Path.Combine(ResultsDir, rawFile), raw.ToString());

        var sumCsv = new StringBuilder();
        if (!weakScaleMode)
            sumCsv.AppendLine("TaskName,InputSize,Ept,LogicalThreads,MedianMs,MeanMs,MinMs,MaxMs,StdDevMs,CvPercent,IsValid");
        else
            sumCsv.AppendLine("TaskName,BaseSize,ThreadCount,ScaledSize,Ept,LogicalThreads,MedianMs,MeanMs,MinMs,MaxMs,StdDevMs,CvPercent,IsValid");

        foreach (var row in summary.OrderBy(x => x.task).ThenBy(x => x.size).ThenByDescending(x => x.ept))
        {
            double cvPercent = row.mean > 0 ? row.stddev / row.mean * 100.0 : double.NaN;
            if (!weakScaleMode)
                sumCsv.AppendLine($"{row.task},{row.size},{row.ept},{row.logicalThreads},{row.median:F4},{row.mean:F4},{row.min:F4},{row.max:F4},{row.stddev:F4},{(double.IsNaN(cvPercent) ? "N/A" : cvPercent.ToString("F2", CultureInfo.InvariantCulture))},{row.valid}");
            else
                sumCsv.AppendLine($"{row.task},{weakBaseSize},{row.threadCount},{row.size},{row.ept},{row.logicalThreads},{row.median:F4},{row.mean:F4},{row.min:F4},{row.max:F4},{row.stddev:F4},{(double.IsNaN(cvPercent) ? "N/A" : cvPercent.ToString("F2", CultureInfo.InvariantCulture))},{row.valid}");
        }
        File.WriteAllText(Path.Combine(ResultsDir, summaryFile), sumCsv.ToString());

        var analysis = new StringBuilder();
        if (!weakScaleMode)
            analysis.AppendLine("TaskName,InputSize,Ept,LogicalThreads,MedianMs,BaselineEpt,BaselineMedianMs,SpeedupVsBaseline,IsValid");
        else
            analysis.AppendLine("TaskName,BaseSize,ThreadCount,ScaledSize,Ept,LogicalThreads,MedianMs,BaselineEpt,BaselineMedianMs,SpeedupVsBaseline,IsValid");
        foreach (var group in summary.GroupBy(x => (x.task, x.size)).OrderBy(g => g.Key.task).ThenBy(g => g.Key.size))
        {
            var baseline = group.FirstOrDefault(x => x.ept == baselineEpt);
            if (baseline == default || baseline.median <= 0) continue;
            foreach (var row in group.OrderByDescending(x => x.ept))
            {
                double speedup = row.median > 0 ? baseline.median / row.median : double.NaN;
                if (!weakScaleMode)
                    analysis.AppendLine($"{row.task},{row.size},{row.ept},{row.logicalThreads},{row.median:F4},{baselineEpt},{baseline.median:F4},{(double.IsNaN(speedup) ? "N/A" : speedup.ToString("F4", CultureInfo.InvariantCulture))},{row.valid}");
                else
                    analysis.AppendLine($"{row.task},{weakBaseSize},{row.threadCount},{row.size},{row.ept},{row.logicalThreads},{row.median:F4},{baselineEpt},{baseline.median:F4},{(double.IsNaN(speedup) ? "N/A" : speedup.ToString("F4", CultureInfo.InvariantCulture))},{row.valid}");
            }
        }
        File.WriteAllText(Path.Combine(ResultsDir, analysisFile), analysis.ToString());
        Console.WriteLine($"\n=== ILGPU scaling run complete ===\nResults in: {Path.GetFullPath(ResultsDir)}");
    }

    private static void PrintRunConfiguration(
        bool quickMode,
        int? runsOverride,
        int[]? customMatrixSizes,
        int[]? customGolSizes,
        VariantSelection? variantSelection,
        int[] threadCounts,
        bool gpuAvailable,
        int? selectedDeviceIndex,
        bool computeSharpAvailable,
        bool weakScaleMode,
        int weakBaseSize,
        bool weakGustafsonStrict)
    {
        if (quickMode) Console.WriteLine("QUICK MODE - small sizes, minimal runs");
        if (runsOverride.HasValue) Console.WriteLine($"Measured runs overridden to: {runsOverride.Value}");
        if (customMatrixSizes != null) Console.WriteLine($"Custom matrix sizes: [{string.Join(", ", customMatrixSizes)}]");
        if (customGolSizes != null) Console.WriteLine($"Custom GoL sizes:    [{string.Join(", ", customGolSizes)}]");
        if (variantSelection != null) Console.WriteLine($"Variant filter:      {variantSelection}");

        string gpuLabel = gpuAvailable
            ? GpuSelector.GetAvailableDevices().ElementAtOrDefault(selectedDeviceIndex!.Value)?.Name ?? "Unknown"
            : "None";
        string computeSharpLabel = computeSharpAvailable ? GraphicsDevice.GetDefault().Name : "None";
        Console.WriteLine($"GPU available: {gpuAvailable} - selected device: {gpuLabel}");
        Console.WriteLine($"ComputeSharp available: {computeSharpAvailable} - device: {computeSharpLabel}");
        Console.WriteLine($"Thread counts to test: [{string.Join(", ", threadCounts)}]");

        if (weakScaleMode)
        {
            Console.WriteLine("WEAK-SCALE MODE ENABLED");
            Console.WriteLine($"  Base size per unit: {weakBaseSize}");
            Console.WriteLine("  MatrixMultiply scaling: n = base * P^(1/3)");
            Console.WriteLine("  GameOfLife scaling:    n = base * sqrt(P)");
            Console.WriteLine($"  Gustafson analysis:    {(weakGustafsonStrict ? "STRICT" : "APPROX")}");
        }

        Console.WriteLine();
    }

    private static void PrintDevices()
    {
        Console.WriteLine("Available ILGPU accelerator devices:");
        var devs = GpuSelector.GetAvailableDevices();
        if (devs.Count == 0)
        {
            Console.WriteLine("  (none found)");
            return;
        }

        foreach (var d in devs)
        {
            string tag = d.Type == ILGPU.Runtime.AcceleratorType.CPU ? " [CPU emulator fallback]" : string.Empty;
            Console.WriteLine($"  [{d.Index}] {d.Name}");
            Console.WriteLine($"       Type: {d.Type}, Memory: {d.MemoryMb} MB, MaxThreadsPerGroup: {d.MaxThreads}{tag}");
        }
    }
}
