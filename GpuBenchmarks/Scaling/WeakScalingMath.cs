namespace GpuBenchmarks.Scaling;

internal static class WeakScalingMath
{
    // Keep formulas identical to preserve thesis results comparability.
    public static int ComputeScaledSize(int baseSize, int threadCount, string taskName)
    {
        if (threadCount <= 1)
        {
            return baseSize;
        }

        if (taskName == "MatrixMultiply")
        {
            double scale = Math.Pow(threadCount, 1.0 / 3.0);
            return Math.Max(1, (int)Math.Round(baseSize * scale));
        }

        if (taskName == "GameOfLife")
        {
            double scale = Math.Sqrt(threadCount);
            return Math.Max(1, (int)Math.Round(baseSize * scale));
        }

        return baseSize;
    }
}
