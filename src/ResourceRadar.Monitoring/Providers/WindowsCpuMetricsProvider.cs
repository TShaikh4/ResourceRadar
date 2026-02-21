using System.Diagnostics;
using ResourceRadar.Monitoring.Abstractions;
using ResourceRadar.Monitoring.Models;

namespace ResourceRadar.Monitoring.Providers;

public sealed class WindowsCpuMetricsProvider : ICpuMetricsProvider, IDisposable
{
    private readonly PerformanceCounter _totalCounter;
    private readonly IReadOnlyList<PerformanceCounter> _coreCounters;

    public WindowsCpuMetricsProvider()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows CPU provider can only run on Windows.");
        }

        _totalCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);

        var category = new PerformanceCounterCategory("Processor");
        var instances = category
            .GetInstanceNames()
            .Where(static name =>
                !name.Equals("_Total", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(name, out _))
            .OrderBy(static name => int.Parse(name))
            .ToArray();

        _coreCounters = instances
            .Select(static instance => new PerformanceCounter("Processor", "% Processor Time", instance, readOnly: true))
            .ToArray();

        // Performance counters need one warm-up read before values are meaningful.
        _ = _totalCounter.NextValue();
        foreach (var coreCounter in _coreCounters)
        {
            _ = coreCounter.NextValue();
        }
    }

    public ValueTask<CpuSample> GetCpuSampleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var total = Math.Clamp(_totalCounter.NextValue(), 0f, 100f);

        var perCore = _coreCounters.Count == 0
            ? new[] { (double)total }
            : _coreCounters
                .Select(static counter => (double)Math.Clamp(counter.NextValue(), 0f, 100f))
                .ToArray();

        return ValueTask.FromResult(new CpuSample(
            TotalUsagePercent: total,
            PerCoreUsagePercent: perCore,
            Timestamp: DateTimeOffset.UtcNow));
    }

    public void Dispose()
    {
        _totalCounter.Dispose();
        foreach (var coreCounter in _coreCounters)
        {
            coreCounter.Dispose();
        }
    }
}
