using ResourceRadar.Monitoring.Models;

namespace ResourceRadar.Monitoring.Abstractions;

public interface IProcessMetricsProvider
{
    ValueTask<ProcessSample> GetProcessSampleAsync(CancellationToken cancellationToken);
}
