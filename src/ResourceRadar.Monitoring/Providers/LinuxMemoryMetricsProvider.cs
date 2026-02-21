using System.Globalization;
using ResourceRadar.Monitoring.Abstractions;
using ResourceRadar.Monitoring.Models;

namespace ResourceRadar.Monitoring.Providers;

public sealed class LinuxMemoryMetricsProvider : IMemoryMetricsProvider
{
    public async ValueTask<MemorySample> GetMemorySampleAsync(CancellationToken cancellationToken)
    {
        var memInfo = await File.ReadAllLinesAsync("/proc/meminfo", cancellationToken).ConfigureAwait(false);

        ulong totalKb = 0;
        ulong availableKb = 0;
        ulong freeKb = 0;

        foreach (var line in memInfo)
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
            {
                totalKb = ExtractKilobytes(line);
                continue;
            }

            if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
            {
                availableKb = ExtractKilobytes(line);
                continue;
            }

            if (line.StartsWith("MemFree:", StringComparison.Ordinal))
            {
                freeKb = ExtractKilobytes(line);
            }
        }

        if (availableKb == 0)
        {
            availableKb = freeKb;
        }

        var totalBytes = totalKb * 1024;
        var availableBytes = Math.Min(availableKb * 1024, totalBytes);
        var usedBytes = totalBytes >= availableBytes ? totalBytes - availableBytes : 0;

        return new MemorySample(
            TotalBytes: totalBytes,
            UsedBytes: usedBytes,
            AvailableBytes: availableBytes,
            Timestamp: DateTimeOffset.UtcNow);
    }

    private static ulong ExtractKilobytes(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 && ulong.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }
}
