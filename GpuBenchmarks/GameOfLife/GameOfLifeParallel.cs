namespace GpuBenchmarks.GameOfLife;

public class GameOfLifeParallel : IBenchmark
{
    private readonly int _threadCount;
    private int _size;
    private int[]? _current, _next;

    public string Name => $"GameOfLife_Parallel_{_threadCount}";

    public GameOfLifeParallel(int threadCount)
    {
        _threadCount = threadCount;
    }

    public void Setup(int size)
    {
        _size = size;
        int total = size * size;
        _current = new int[total];
        _next = new int[total];

        var rng = new Random(42);
        for (int i = 0; i < total; i++)
            _current[i] = rng.Next(2);
    }

    public void Run()
    {
        int n = _size;
        int[] current = _current!;
        int[] next = _next!;
        var options = new ParallelOptions { MaxDegreeOfParallelism = _threadCount };

        for (int step = 0; step < 100; step++)
        {
            int[] cap_current = current;
            int[] cap_next = next;

            System.Threading.Tasks.Parallel.For(0, n, options, y =>
            {
                for (int x = 0; x < n; x++)
                {
                    int neighbors = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = (x + dx + n) % n;
                            int ny = (y + dy + n) % n;
                            neighbors += cap_current[ny * n + nx];
                        }
                    }
                    bool alive = cap_current[y * n + x] == 1;
                    cap_next[y * n + x] = alive
                        ? ((neighbors == 2 || neighbors == 3) ? 1 : 0)
                        : (neighbors == 3 ? 1 : 0);
                }
            });

            // Swap
            var tmp = current;
            current = next;
            next = tmp;
        }

        _current = current;
        _next = next;
    }

    public float[] GetResult()
    {
        var result = new float[_current!.Length];
        for (int i = 0; i < _current.Length; i++)
            result[i] = _current[i];
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
        _current = null;
        _next = null;
    }
}

