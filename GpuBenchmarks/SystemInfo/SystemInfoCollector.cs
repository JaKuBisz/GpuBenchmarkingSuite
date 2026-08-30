using System.Runtime.InteropServices;
using System.Text;
using ILGPU;

namespace GpuBenchmarks.SystemInfo;

internal static class SystemInfoCollector
{
    public static string Collect()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== System Information ===");
        sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Machine: {Environment.MachineName}");
        sb.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        sb.AppendLine($"OS Architecture: {RuntimeInformation.OSArchitecture}");
        sb.AppendLine($".NET Version: {Environment.Version}");
        sb.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"Processor Count: {Environment.ProcessorCount}");
        sb.AppendLine($"64-bit Process: {Environment.Is64BitProcess}");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            CollectWindowsHardwareInfo(sb);
        }
        else
        {
            CollectLinuxHardwareInfo(sb);
        }

        sb.AppendLine($"ILGPU Version: {typeof(Context).Assembly.GetName().Version}");
        var devices = GpuSelector.GetAvailableDevices();
        if (devices.Count == 0)
        {
            sb.AppendLine("ILGPU Devices: none found");
        }
        else
        {
            foreach (var d in devices)
            {
                sb.AppendLine($"  [{d.Index}] {d.Name} | Type: {d.Type} | Memory: {d.MemoryMb} MB | MaxThreadsPerGroup: {d.MaxThreads}");
            }
        }

        return sb.ToString();
    }

    private static void CollectWindowsHardwareInfo(StringBuilder sb)
    {
        try
        {
            var managementType = Type.GetType("System.Management.ManagementObjectSearcher, System.Management");
            if (managementType == null)
            {
                sb.AppendLine("CPU: (System.Management not loaded)");
                return;
            }

            dynamic cpuSearcher = Activator.CreateInstance(managementType, "SELECT Name FROM Win32_Processor")!;
            foreach (dynamic obj in cpuSearcher.Get())
            {
                sb.AppendLine($"CPU: {obj["Name"]}");
            }
            cpuSearcher.Dispose();

            dynamic gpuSearcher = Activator.CreateInstance(managementType, "SELECT Name, AdapterRAM FROM Win32_VideoController")!;
            foreach (dynamic obj in gpuSearcher.Get())
            {
                long vram = Convert.ToInt64(obj["AdapterRAM"] ?? 0L);
                sb.AppendLine($"GPU (WMI): {obj["Name"]}, VRAM: {vram / (1024 * 1024)} MB");
            }
            gpuSearcher.Dispose();
        }
        catch (Exception ex)
        {
            sb.AppendLine($"CPU/GPU (WMI): unavailable - {ex.Message}");
        }
    }

    private static void CollectLinuxHardwareInfo(StringBuilder sb)
    {
        try
        {
            var cpuInfo = File.ReadAllText("/proc/cpuinfo");
            var modelLine = cpuInfo.Split('\n').FirstOrDefault(l => l.StartsWith("model name"));
            sb.AppendLine($"CPU: {modelLine?.Split(':')[1].Trim() ?? "unknown"}");
        }
        catch
        {
            sb.AppendLine("CPU: (/proc/cpuinfo unavailable)");
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("lspci", "")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            using var process = System.Diagnostics.Process.Start(psi);
            var output = process?.StandardOutput.ReadToEnd() ?? string.Empty;
            foreach (var line in output.Split('\n').Where(l => l.Contains("VGA") || l.Contains("3D")))
            {
                sb.AppendLine($"GPU: {line.Trim()}");
            }
        }
        catch
        {
            sb.AppendLine("GPU: (lspci unavailable)");
        }
    }
}
