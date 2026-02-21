namespace ResourceRadar.Monitoring.Models;

public sealed record CpuSample(
    double TotalUsagePercent,
    IReadOnlyList<double> PerCoreUsagePercent,
    DateTimeOffset Timestamp);
