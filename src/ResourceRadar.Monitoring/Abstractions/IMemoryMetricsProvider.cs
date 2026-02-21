using ResourceRadar.Monitoring.Models;

namespace ResourceRadar.Monitoring.Abstractions;

public interface IMemoryMetricsProvider
{
    ValueTask<MemorySample> GetMemorySampleAsync(CancellationToken cancellationToken);
}
