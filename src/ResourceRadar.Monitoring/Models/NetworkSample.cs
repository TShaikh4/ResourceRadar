namespace ResourceRadar.Monitoring.Models;

public sealed record NetworkSample(
    ulong TotalBytesSent,
    ulong TotalBytesReceived,
    double UploadBytesPerSecond,
    double DownloadBytesPerSecond,
    int ActiveInterfaceCount,
    DateTimeOffset Timestamp);
