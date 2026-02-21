using System.Net.NetworkInformation;
using ResourceRadar.Monitoring.Abstractions;
using ResourceRadar.Monitoring.Models;

namespace ResourceRadar.Monitoring.Providers;

public sealed class NetworkMetricsProvider : INetworkMetricsProvider
{
    private ulong? _previousTotalSent;
    private ulong? _previousTotalReceived;
    private DateTimeOffset? _previousTimestamp;

    public ValueTask<NetworkSample> GetNetworkSampleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ulong totalSent = 0;
        ulong totalReceived = 0;
        var activeInterfaces = 0;

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var stats = networkInterface.GetIPv4Statistics();
                if (stats.BytesSent < 0 || stats.BytesReceived < 0)
                {
                    continue;
                }

                totalSent += (ulong)stats.BytesSent;
                totalReceived += (ulong)stats.BytesReceived;
                activeInterfaces++;
            }
            catch
            {
                // Ignore interfaces that cannot expose stats in the current permission/state.
            }
        }

        var now = DateTimeOffset.UtcNow;
        var uploadBytesPerSecond = 0.0;
        var downloadBytesPerSecond = 0.0;

        if (_previousTimestamp is { } previousTimestamp &&
            _previousTotalSent is { } previousTotalSent &&
            _previousTotalReceived is { } previousTotalReceived)
        {
            var elapsedSeconds = (now - previousTimestamp).TotalSeconds;
            if (elapsedSeconds > 0)
            {
                var deltaSent = totalSent >= previousTotalSent ? totalSent - previousTotalSent : 0;
                var deltaReceived = totalReceived >= previousTotalReceived ? totalReceived - previousTotalReceived : 0;

                uploadBytesPerSecond = deltaSent / elapsedSeconds;
                downloadBytesPerSecond = deltaReceived / elapsedSeconds;
            }
        }

        _previousTimestamp = now;
        _previousTotalSent = totalSent;
        _previousTotalReceived = totalReceived;

        return ValueTask.FromResult(new NetworkSample(
            TotalBytesSent: totalSent,
            TotalBytesReceived: totalReceived,
            UploadBytesPerSecond: uploadBytesPerSecond,
            DownloadBytesPerSecond: downloadBytesPerSecond,
            ActiveInterfaceCount: activeInterfaces,
            Timestamp: now));
    }
}
