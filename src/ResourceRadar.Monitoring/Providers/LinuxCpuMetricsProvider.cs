using ResourceRadar.Monitoring.Abstractions;
using ResourceRadar.Monitoring.Internal;
using ResourceRadar.Monitoring.Models;

namespace ResourceRadar.Monitoring.Providers;

public sealed class LinuxCpuMetricsProvider : ICpuMetricsProvider
{
    private CpuTimesSnapshot? _previousTotal;
    private readonly Dictionary<int, CpuTimesSnapshot> _previousPerCore = new();

    public async ValueTask<CpuSample> GetCpuSampleAsync(CancellationToken cancellationToken)
    {
        var statContents = await File.ReadAllTextAsync("/proc/stat", cancellationToken).ConfigureAwait(false);
        var snapshot = LinuxProcStatParser.Parse(statContents);

        var totalUsage = _previousTotal is { } previousTotal
            ? CpuTimesSnapshot.CalculateUsagePercent(previousTotal, snapshot.Total)
            : 0;

        var perCore = new List<double>(snapshot.PerCore.Count);
        foreach (var core in snapshot.PerCore.OrderBy(pair => pair.Key))
        {
            // Per-core usage is computed from successive snapshots for each cpuN line.
            var usage = _previousPerCore.TryGetValue(core.Key, out var previousCore)
                ? CpuTimesSnapshot.CalculateUsagePercent(previousCore, core.Value)
                : totalUsage;

            _previousPerCore[core.Key] = core.Value;
            perCore.Add(usage);
        }

        _previousTotal = snapshot.Total;

        return new CpuSample(
            TotalUsagePercent: totalUsage,
            PerCoreUsagePercent: perCore,
            Timestamp: DateTimeOffset.UtcNow);
    }
}
