using System.Diagnostics;

namespace GpuBenchmarks.Tests.Integration;

public class SmokeTests
{
    private static string RepoRoot => ResolveRepoRoot();

    private static string ProjectPath => Path.Combine(RepoRoot, "GpuBenchmarks", "GpuBenchmarks.csproj");
    private static string ResultsDir => Path.Combine(TestRunDir, "results");
    private static string TestRunDir => Path.Combine(Path.GetTempPath(), "GpuBenchmarks.Tests.Run");

    [Fact]
    public void StrongQuickRun_GeneratesExpectedFiles()
    {
        CleanResults();
        int code = RunApp("--quick --variants seq,parallel_1 --sizes 64 --gol-sizes 8");

        Assert.Equal(0, code);
        Assert.True(File.Exists(Path.Combine(ResultsDir, "raw_results.csv")));
        Assert.True(File.Exists(Path.Combine(ResultsDir, "summary.csv")));
        Assert.True(File.Exists(Path.Combine(ResultsDir, "parallel1_analysis.csv")));
        Assert.True(File.Exists(Path.Combine(ResultsDir, "amdahl_analysis.csv")));
    }

    [Fact]
    public void WeakQuickRun_GeneratesExpectedFiles()
    {
        CleanResults();
        int code = RunApp("--quick --weak-scale --weak-base 64 --variants seq,parallel_1");

        Assert.Equal(0, code);
        Assert.True(File.Exists(Path.Combine(ResultsDir, "weak_raw_results.csv")));
        Assert.True(File.Exists(Path.Combine(ResultsDir, "weak_summary.csv")));
        Assert.True(File.Exists(Path.Combine(ResultsDir, "weak_gustafson_analysis.csv")));
    }

    [Fact]
    public void RecalcModes_WorkFromRawCsv()
    {
        CleanResults();
        _ = RunApp("--quick --variants seq,parallel_1 --sizes 64 --gol-sizes 8");
        _ = RunApp("--quick --weak-scale --weak-base 64 --variants seq,parallel_1");

        Assert.Equal(0, RunApp("--recalc-strong"));
        Assert.Equal(0, RunApp("--recalc-weak"));
    }

    private static int RunApp(string appArgs)
    {
        Directory.CreateDirectory(TestRunDir);
        var psi = new ProcessStartInfo("dotnet", $"run -c Release --project \"{ProjectPath}\" -- {appArgs}")
        {
            WorkingDirectory = TestRunDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi)!;
        process.WaitForExit((int)TimeSpan.FromMinutes(3).TotalMilliseconds);
        return process.ExitCode;
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            string slnPath = Path.Combine(current.FullName, "GpuBenchmarks.sln");
            if (File.Exists(slnPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate GpuBenchmarks.sln from test base directory.");
    }

    private static void CleanResults()
    {
        Directory.CreateDirectory(ResultsDir);
        foreach (var file in Directory.GetFiles(ResultsDir, "*.csv"))
        {
            File.Delete(file);
        }
    }
}
