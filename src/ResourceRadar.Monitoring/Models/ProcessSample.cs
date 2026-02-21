namespace ResourceRadar.Monitoring.Models;

public sealed record ProcessMetrics(
    int ProcessId,
    string ProcessName,
    double CpuPercent,
    ulong MemoryBytes,
    int TrafficConnectionCount);

public sealed record ProcessSample(
    IReadOnlyList<ProcessMetrics> Processes,
    DateTimeOffset Timestamp);
