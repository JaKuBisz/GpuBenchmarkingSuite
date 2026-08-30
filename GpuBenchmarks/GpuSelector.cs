using ILGPU;
using ILGPU.Runtime;

namespace GpuBenchmarks;

/// <summary>
/// Manages GPU/accelerator device discovery and selection.
/// Supports: CUDA (NVIDIA), OpenCL (AMD/Intel/any), CPU fallback.
/// </summary>
public static class GpuSelector
{
    public record DeviceInfo(int Index, string Name, AcceleratorType Type, long MemoryMb, int MaxThreads);

    private static List<DeviceInfo>? _cachedDevices;

    /// <summary>
    /// Returns all available ILGPU devices (including CPU accelerator if present).
    /// </summary>
    public static List<DeviceInfo> GetAvailableDevices()
    {
        if (_cachedDevices != null) return _cachedDevices;

        _cachedDevices = new List<DeviceInfo>();
        try
        {
            using var ctx = Context.CreateDefault();
            int idx = 0;
            foreach (var device in ctx)
            {
                _cachedDevices.Add(new DeviceInfo(
                    idx++,
                    device.Name,
                    device.AcceleratorType,
                    device.MemorySize / (1024 * 1024),
                    device.MaxNumThreadsPerGroup));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Could not enumerate ILGPU devices: {ex.Message}");
        }

        return _cachedDevices;
    }

    /// <summary>
    /// Returns true if at least one non-CPU accelerator is available.
    /// </summary>
    public static bool HasGpuDevice =>
        GetAvailableDevices().Any(d => d.Type != AcceleratorType.CPU);

    /// <summary>
    /// Selects a device index from CLI args (--gpu N), environment variable
    /// (GPUBENCH_GPU=N), or interactively prompts the user.
    /// Pass args from Main(). Returns null if no GPU available.
    /// </summary>
    public static int? SelectDevice(string[] args)
    {
        var devices = GetAvailableDevices();
        var gpuDevices = devices.Where(d => d.Type != AcceleratorType.CPU).ToList();

        if (gpuDevices.Count == 0)
        {
            Console.WriteLine("[INFO] No GPU/OpenCL accelerators found. GPU benchmarks will be skipped.");
            return null;
        }

        int? cliSelection = TryGetCliGpuIndex(args, devices.Count);
        if (cliSelection.HasValue)
        {
            Console.WriteLine($"[INFO] GPU selected via --gpu argument: [{cliSelection.Value}] {devices[cliSelection.Value].Name}");
            return cliSelection.Value;
        }

        int? envSelection = TryGetEnvGpuIndex(devices.Count);
        if (envSelection.HasValue)
        {
            Console.WriteLine($"[INFO] GPU selected via GPUBENCH_GPU env var: [{envSelection.Value}] {devices[envSelection.Value].Name}");
            return envSelection.Value;
        }

        // 3. If only one GPU, use it automatically
        if (gpuDevices.Count == 1)
        {
            Console.WriteLine($"[INFO] Single GPU detected, using automatically: {gpuDevices[0].Name}");
            return gpuDevices[0].Index;
        }

        // 4. Interactive selection
        Console.WriteLine("\nAvailable accelerator devices:");
        foreach (var d in devices)
        {
            string marker = d.Type == AcceleratorType.CPU ? " [CPU emulator]" : "";
            Console.WriteLine($"  [{d.Index}] {d.Name}  ({d.Type}, {d.MemoryMb} MB){marker}");
        }
        Console.Write($"\nSelect GPU index to use (0-{devices.Count - 1}), or press Enter for default [{gpuDevices[0].Index}]: ");
        var input = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(input))
        {
            Console.WriteLine($"[INFO] Using default: [{gpuDevices[0].Index}] {gpuDevices[0].Name}");
            return gpuDevices[0].Index;
        }

        if (int.TryParse(input, out int chosen) && chosen >= 0 && chosen < devices.Count)
        {
            Console.WriteLine($"[INFO] Selected: [{chosen}] {devices[chosen].Name}");
            return chosen;
        }

        Console.WriteLine($"[WARN] Invalid selection, using default: [{gpuDevices[0].Index}] {gpuDevices[0].Name}");
        return gpuDevices[0].Index;
    }

    private static int? TryGetCliGpuIndex(string[] args, int deviceCount)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--gpu", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out int index) &&
                index >= 0 &&
                index < deviceCount)
            {
                return index;
            }
        }

        return null;
    }

    private static int? TryGetEnvGpuIndex(int deviceCount)
    {
        string? envVal = Environment.GetEnvironmentVariable("GPUBENCH_GPU");
        if (envVal != null && int.TryParse(envVal, out int index) && index >= 0 && index < deviceCount)
        {
            return index;
        }

        return null;
    }

    /// <summary>
    /// Creates an Accelerator for the given device index.
    /// Caller is responsible for disposing both Context and Accelerator.
    /// </summary>
    public static (Context ctx, Accelerator acc) CreateAccelerator(int deviceIndex)
    {
        var ctx = Context.CreateDefault();
        int idx = 0;
        foreach (var device in ctx)
        {
            if (idx == deviceIndex)
                return (ctx, device.CreateAccelerator(ctx));
            idx++;
        }
        ctx.Dispose();
        throw new ArgumentOutOfRangeException(nameof(deviceIndex),
            $"Device index {deviceIndex} out of range (found {idx} devices)");
    }
}
