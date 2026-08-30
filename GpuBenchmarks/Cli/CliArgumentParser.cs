using System.Globalization;

namespace GpuBenchmarks.Cli;

internal static class CliArgumentParser
{
    public static int? TryGetPositiveIntArg(string[] args, string name)
    {
        int? result = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Length)
            {
                Console.WriteLine($"[WARN] Missing value for {name}");
                continue;
            }

            string rawValue = args[i + 1];
            if (int.TryParse(rawValue, out int parsed) && parsed > 0)
            {
                result = parsed;
                continue;
            }

            Console.WriteLine($"[WARN] Ignoring invalid value for {name}: {rawValue}");
        }

        return result;
    }

    public static int[]? TryGetPositiveIntListArg(string[] args, string name)
    {
        int[]? result = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Length)
            {
                Console.WriteLine($"[WARN] Missing value for {name}");
                continue;
            }

            string rawValue = args[i + 1];
            var values = new List<int>();
            bool isValid = true;

            foreach (var part in rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
                {
                    Console.WriteLine($"[WARN] Ignoring invalid list for {name}: {rawValue}");
                    isValid = false;
                    break;
                }

                values.Add(parsed);
            }

            if (!isValid || values.Count == 0)
            {
                continue;
            }

            result = values.ToArray();
        }

        return result;
    }

    public static string? TryGetStringArg(string[] args, string name)
    {
        string? result = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Length)
            {
                Console.WriteLine($"[WARN] Missing value for {name}");
                continue;
            }

            result = args[i + 1];
        }

        return result;
    }
}
