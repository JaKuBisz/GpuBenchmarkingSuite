namespace GpuBenchmarks.MatrixMultiply;

public class MatrixMultiplySequential : IBenchmark
{
    public string Name => "MatrixMultiply_Sequential";
    private int _size;
    private float[]? _a, _b, _c;

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
        float[] a = _a!, b = _b!, c = _c!;
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                float sum = 0f;
                for (int k = 0; k < n; k++)
                    sum += a[i * n + k] * b[k * n + j];
                c[i * n + j] = sum;
            }
        }
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
        _a = null;
        _b = null;
        _c = null;
    }
}

