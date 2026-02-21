using System.Globalization;

namespace ResourceRadar.Monitoring.Internal;

internal static class LinuxProcStatParser
{
    internal sealed record Snapshot(CpuTimesSnapshot Total, IReadOnlyDictionary<int, CpuTimesSnapshot> PerCore);

    public static Snapshot Parse(string statContents)
    {
        CpuTimesSnapshot? total = null;
        var perCore = new Dictionary<int, CpuTimesSnapshot>();

        foreach (var line in statContents.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith("cpu", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 5)
            {
                continue;
            }

            if (!TryCreateSnapshot(parts, out var snapshot))
            {
                continue;
            }

            if (parts[0].Equals("cpu", StringComparison.Ordinal))
            {
                total = snapshot;
                continue;
            }

            if (parts[0].Length > 3 && int.TryParse(parts[0].AsSpan(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out var coreIndex))
            {
                perCore[coreIndex] = snapshot;
            }
        }

        return new Snapshot(total ?? default, perCore);
    }

    private static bool TryCreateSnapshot(string[] parts, out CpuTimesSnapshot snapshot)
    {
        snapshot = default;
        ulong total = 0;
        ulong idle = 0;

        for (var i = 1; i < parts.Length; i++)
        {
            if (!ulong.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            total += value;
            if (i == 4)
            {
                idle += value;
            }

            if (i == 5)
            {
                idle += value;
            }
        }

        snapshot = new CpuTimesSnapshot(total, idle);
        return true;
    }
}
