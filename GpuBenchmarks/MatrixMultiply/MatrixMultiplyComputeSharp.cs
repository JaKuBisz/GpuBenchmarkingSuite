using ComputeSharp;

namespace GpuBenchmarks.MatrixMultiply;

/// <summary>
/// Matrix multiplication benchmark using ComputeSharp (DirectX 12 / HLSL).
/// </summary>
public class MatrixMultiplyComputeSharp : IBenchmark
{
    public string Name => "MatrixMultiply_GPU_ComputeSharp";

    private int _size;
    private float[]? _a, _b, _c;

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
        _a = new float[total];
        _b = new float[total];
        _c = new float[total];

        var rng = new Random(42);
        for (int i = 0; i < total; i++)
        {
            _a[i] = (float)rng.NextDouble();
            _b[i] = (float)rng.NextDouble();
        }
    }

    public void Run()
    {
        int n = _size;
        var device = GraphicsDevice.GetDefault();

        using var bufA = device.AllocateReadOnlyBuffer<float>(_a!);
        using var bufB = device.AllocateReadOnlyBuffer<float>(_b!);
        using var bufC = device.AllocateReadWriteBuffer<float>(n * n);

        device.For(n, n, new MatMulShader(bufA, bufB, bufC, n));

        bufC.CopyTo(_c!);
    }

    public float[] GetResult() => _c!;

    public bool Validate(IBenchmark reference)
    {
        float[] mine = _c!;
        float[] other = reference.GetResult();
        int checkCount = Math.Min(100, mine.Length);
        for (int i = 0; i < checkCount; i++)
            if (Math.Abs(mine[i] - other[i]) > 1e-2f)
                return false;
        return true;
    }

    public void Cleanup() { }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct MatMulShader : IComputeShader
{
    public readonly ReadOnlyBuffer<float> A;
    public readonly ReadOnlyBuffer<float> B;
    public readonly ReadWriteBuffer<float> C;
    public readonly int N;

    public MatMulShader(ReadOnlyBuffer<float> a, ReadOnlyBuffer<float> b, ReadWriteBuffer<float> c, int n)
    {
        A = a;
        B = b;
        C = c;
        N = n;
    }

    public void Execute()
    {
        int row = ThreadIds.Y;
        int col = ThreadIds.X;
        float sum = 0f;
        for (int k = 0; k < N; k++)
            sum += A[row * N + k] * B[k * N + col];
        C[row * N + col] = sum;
    }
}
