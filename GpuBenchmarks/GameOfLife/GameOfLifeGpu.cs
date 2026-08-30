using ILGPU;
using ILGPU.Runtime;

namespace GpuBenchmarks.GameOfLife;

public class GameOfLifeGpu : IBenchmark
{
    public string Name => "GameOfLife_GPU";
    private readonly int _deviceIndex;
    private readonly int _elementsPerThread;
    private int _size;
    private int[]? _initialGrid;
    private int[]? _result;
    private Context? _context;
    private Accelerator? _accelerator;
    private Action<Index1D, ArrayView1D<int, Stride1D.Dense>, ArrayView1D<int, Stride1D.Dense>, int, int>? _kernel;

    public static bool IsAvailable => GpuBenchmarks.GpuSelector.HasGpuDevice;

    public GameOfLifeGpu(int deviceIndex = 0, int elementsPerThread = 1)
    {
        _deviceIndex = deviceIndex;
        _elementsPerThread = Math.Max(1, elementsPerThread);
    }

    public void Setup(int size)
    {
        _size = size;
        int total = size * size;
        _initialGrid = new int[total];
        _result = new int[total];

        var rng = new Random(42);
        for (int i = 0; i < total; i++)
            _initialGrid[i] = rng.Next(2);

        (_context, _accelerator) = GpuBenchmarks.GpuSelector.CreateAccelerator(_deviceIndex);
        _kernel = _accelerator.LoadAutoGroupedStreamKernel<
            Index1D,
            ArrayView1D<int, Stride1D.Dense>,
            ArrayView1D<int, Stride1D.Dense>,
            int,
            int>(GoLKernel);
    }

    static void GoLKernel(
        Index1D index,
        ArrayView1D<int, Stride1D.Dense> current,
        ArrayView1D<int, Stride1D.Dense> next,
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

            int y = idx / n;
            int x = idx % n;
            int neighbors = 0;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = (x + dx + n) % n;
                    int ny = (y + dy + n) % n;
                    neighbors += current[ny * n + nx];
                }
            }
            bool alive = current[y * n + x] == 1;
            int nextState;
            if (alive)
                nextState = (neighbors == 2 || neighbors == 3) ? 1 : 0;
            else
                nextState = neighbors == 3 ? 1 : 0;
            next[y * n + x] = nextState;
        }
    }

    public void Run()
    {
        int n = _size;
        var acc = _accelerator!;
        int total = n * n;
        int logicalThreads = (total + _elementsPerThread - 1) / _elementsPerThread;

        using var bufA = acc.Allocate1D<int>(total);
        using var bufB = acc.Allocate1D<int>(total);

        bufA.CopyFromCPU(_initialGrid!);

        MemoryBuffer1D<int, Stride1D.Dense> current = bufA;
        MemoryBuffer1D<int, Stride1D.Dense> next = bufB;

        for (int step = 0; step < 100; step++)
        {
            _kernel!(logicalThreads, current.View, next.View, n, _elementsPerThread);
            acc.Synchronize();

            // Swap
            var tmp = current;
            current = next;
            next = tmp;
        }

        current.CopyToCPU(_result!);
    }

    public float[] GetResult()
    {
        var result = new float[_result!.Length];
        for (int i = 0; i < _result.Length; i++)
            result[i] = _result[i];
        return result;
    }

    public bool Validate(IBenchmark reference)
    {
        float[] mine = GetResult();
        float[] other = reference.GetResult();
        if (mine.Length != other.Length) return false;
        for (int i = 0; i < mine.Length; i++)
        {
            if ((int)mine[i] != (int)other[i])
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
        _initialGrid = null;
        _result = null;
    }
}
