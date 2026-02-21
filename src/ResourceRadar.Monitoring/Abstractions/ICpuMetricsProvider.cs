using ResourceRadar.Monitoring.Models;

namespace ResourceRadar.Monitoring.Abstractions;

public interface ICpuMetricsProvider
{
    ValueTask<CpuSample> GetCpuSampleAsync(CancellationToken cancellationToken);
}
