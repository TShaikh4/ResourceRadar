namespace ResourceRadar.Monitoring.Models;

public sealed record MemorySample(
    ulong TotalBytes,
    ulong UsedBytes,
    ulong AvailableBytes,
    DateTimeOffset Timestamp)
{
    public double UsedPercent =>
        TotalBytes == 0 ? 0 : (double)UsedBytes / TotalBytes * 100.0;
}
