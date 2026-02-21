using ResourceRadar.Monitoring.Models;

namespace ResourceRadar.Monitoring.Abstractions;

public interface INetworkMetricsProvider
{
    ValueTask<NetworkSample> GetNetworkSampleAsync(CancellationToken cancellationToken);
}
