using ComputeSharp;

namespace GpuBenchmarks.GameOfLife;

/// <summary>
/// Game of Life benchmark using ComputeSharp (DirectX 12 / HLSL).
/// Runs 100 generations with ping-pong double buffering.
/// </summary>
public class GameOfLifeComputeSharp : IBenchmark
{
    public string Name => "GameOfLife_GPU_ComputeSharp";

    private int _size;
    private int[]? _initialGrid;
    private float[]? _result;

    public static bool IsAvailable
    {
        get
        {
            try { return GraphicsDevice.GetDefault() is not null; }
            catch { return false; }
        }
    }

    public void Setup(int size)
    {
        _size = size;
        int total = size * size;
        _initialGrid = new int[total];
        _result = null;

        var rng = new Random(42);
        for (int i = 0; i < total; i++)
            _initialGrid[i] = rng.Next(2);
    }

    public void Run()
    {
        int n = _size;
        int total = n * n;
        var device = GraphicsDevice.GetDefault();

        using var bufA = device.AllocateReadWriteBuffer<int>(_initialGrid!);
        using var bufB = device.AllocateReadWriteBuffer<int>(total);

        // 100 generation ping-pong:
        //   even step: A(current) → B(next)
        //   odd  step: B(current) → A(next)
        for (int step = 0; step < 100; step++)
        {
            if (step % 2 == 0)
                device.For(n, n, new GoLShader(bufA, bufB, n));
            else
                device.For(n, n, new GoLShader(bufB, bufA, n));
        }

        // After 100 steps (last = step 99, odd) the final state is in bufA.
        int[] raw = new int[total];
        bufA.CopyTo(raw);

        _result = new float[total];
        for (int i = 0; i < total; i++)
            _result[i] = raw[i];
    }

    public float[] GetResult() => _result!;

    public bool Validate(IBenchmark reference)
    {
        float[] mine = _result!;
        float[] other = reference.GetResult();
        if (mine.Length != other.Length) return false;
        for (int i = 0; i < mine.Length; i++)
            if ((int)mine[i] != (int)other[i])
                return false;
        return true;
    }

    public void Cleanup()
    {
        _initialGrid = null;
        _result = null;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct GoLShader : IComputeShader
{
    public readonly ReadWriteBuffer<int> Current;
    public readonly ReadWriteBuffer<int> Next;
    public readonly int N;

    public GoLShader(ReadWriteBuffer<int> current, ReadWriteBuffer<int> next, int n)
    {
        Current = current;
        Next = next;
        N = n;
    }

    public void Execute()
    {
        int x = ThreadIds.Y;
        int y = ThreadIds.X;

        int neighbors = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = (x + dx + N) % N;
                int ny = (y + dy + N) % N;
                neighbors += Current[nx * N + ny];
            }
        }

        bool alive = Current[x * N + y] == 1;
        int nextState;
        if (alive)
            nextState = (neighbors == 2 || neighbors == 3) ? 1 : 0;
        else
            nextState = neighbors == 3 ? 1 : 0;
        Next[x * N + y] = nextState;
    }
}
