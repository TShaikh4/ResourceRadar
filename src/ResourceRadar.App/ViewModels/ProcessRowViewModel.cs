using System.Globalization;

namespace ResourceRadar.App.ViewModels;

public sealed class ProcessRowViewModel
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required double CpuPercent { get; init; }
    public required ulong MemoryBytes { get; init; }
    public required int TrafficConnectionCount { get; init; }

    public string CpuText =>
        string.Format(CultureInfo.InvariantCulture, "{0:0.0}%", CpuPercent);

    public string MemoryText => FormatBytes(MemoryBytes);

    public string TrafficText =>
        string.Format(CultureInfo.InvariantCulture, "{0}", TrafficConnectionCount);

    private static string FormatBytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:0.0} {1}", value, units[unitIndex]);
    }
}
