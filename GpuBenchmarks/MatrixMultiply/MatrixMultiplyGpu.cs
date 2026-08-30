using ILGPU;
using ILGPU.Runtime;

namespace GpuBenchmarks.MatrixMultiply;

public class MatrixMultiplyGpu : IBenchmark
{
    public string Name => "MatrixMultiply_GPU";
    private readonly int _deviceIndex;
    private readonly int _elementsPerThread;
    private int _size;
    private float[]? _a, _b, _c;
    private Context? _context;
    private Accelerator? _accelerator;
    private Action<Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, int, int>? _kernel;

    // Keep for backwards-compat / system info
    public static bool IsAvailable => GpuSelector.HasGpuDevice;
    public static string AcceleratorInfo => GpuSelector.GetAvailableDevices()
        .FirstOrDefault(d => d.Type != ILGPU.Runtime.AcceleratorType.CPU)?.Name ?? "None";

    public MatrixMultiplyGpu(int deviceIndex = 0, int elementsPerThread = 1)
    {
        _deviceIndex = deviceIndex;
        _elementsPerThread = Math.Max(1, elementsPerThread);
    }

    public void Setup(int size)
    {
        _size = size;
        int total = size * size;
        _a = new float[total];
        _b = new float[total];
        _c = new float[total];

        var rng = new Random(42);
        for (int i = 0; i < total; i++)
        {
            _a[i] = (float)rng.NextDouble();
            _b[i] = (float)rng.NextDouble();
        }

        (_context, _accelerator) = GpuSelector.CreateAccelerator(_deviceIndex);
        _kernel = _accelerator.LoadAutoGroupedStreamKernel<
            Index1D,
            ArrayView1D<float, Stride1D.Dense>,
            ArrayView1D<float, Stride1D.Dense>,
            ArrayView1D<float, Stride1D.Dense>,
            int,
            int>(MatMulKernel);
    }

    static void MatMulKernel(
        Index1D index,
        ArrayView1D<float, Stride1D.Dense> a,
        ArrayView1D<float, Stride1D.Dense> b,
        ArrayView1D<float, Stride1D.Dense> c,
        int n,
        int elementsPerThread)
    {
        int total = n * n;
        int start = index * elementsPerThread;
        for (int offset = 0; offset < elementsPerThread; offset++)
        {
            int idx = start + offset;
            if (idx >= total)
                break;

            int row = idx / n;
            int col = idx % n;
            float sum = 0f;
            for (int k = 0; k < n; k++)
                sum += a[row * n + k] * b[k * n + col];
            c[row * n + col] = sum;
        }
    }

    public void Run()
    {
        int n = _size;
        var acc = _accelerator!;
        int total = n * n;
        int logicalThreads = (total + _elementsPerThread - 1) / _elementsPerThread;

        using var bufA = acc.Allocate1D<float>(_a!.Length);
        using var bufB = acc.Allocate1D<float>(_b!.Length);
        using var bufC = acc.Allocate1D<float>(_c!.Length);

        bufA.CopyFromCPU(_a!);
        bufB.CopyFromCPU(_b!);

        _kernel!(logicalThreads, bufA.View, bufB.View, bufC.View, n, _elementsPerThread);
        acc.Synchronize();

        bufC.CopyToCPU(_c!);
    }

    public float[] GetResult() => _c!;

    public bool Validate(IBenchmark reference)
    {
        float[] mine = _c!;
        float[] other = reference.GetResult();
        int checkCount = Math.Min(100, mine.Length);
        for (int i = 0; i < checkCount; i++)
        {
            if (Math.Abs(mine[i] - other[i]) > 1e-2f)
                return false;
        }
        return true;
    }

    public void Cleanup()
    {
        _kernel = null;
        _accelerator?.Dispose();
        _accelerator = null;
        _context?.Dispose();
        _context = null;
        _a = null;
        _b = null;
        _c = null;
    }
}

