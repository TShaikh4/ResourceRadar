using ResourceRadar.Monitoring.Models;

namespace ResourceRadar.Monitoring.Abstractions;

public interface IMetricsPoller : IAsyncDisposable
{
    event EventHandler<CpuSample>? CpuSampleReceived;
    event EventHandler<MemorySample>? MemorySampleReceived;
    event EventHandler<NetworkSample>? NetworkSampleReceived;
    event EventHandler<ProcessSample>? ProcessSampleReceived;
    event EventHandler<Exception>? PollerFaulted;

    Task StartAsync(TimeSpan interval, CancellationToken cancellationToken = default);
    Task StopAsync();
}
