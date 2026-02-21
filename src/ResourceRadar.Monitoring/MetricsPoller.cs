using ResourceRadar.Monitoring.Abstractions;
using ResourceRadar.Monitoring.Models;

namespace ResourceRadar.Monitoring;

public sealed class MetricsPoller : IMetricsPoller
{
    private readonly ICpuMetricsProvider _cpuMetricsProvider;
    private readonly IMemoryMetricsProvider _memoryMetricsProvider;
    private readonly INetworkMetricsProvider _networkMetricsProvider;
    private readonly IProcessMetricsProvider _processMetricsProvider;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _internalCts;
    private Task? _runTask;

    public MetricsPoller(
        ICpuMetricsProvider cpuMetricsProvider,
        IMemoryMetricsProvider memoryMetricsProvider,
        INetworkMetricsProvider networkMetricsProvider,
        IProcessMetricsProvider processMetricsProvider)
    {
        _cpuMetricsProvider = cpuMetricsProvider;
        _memoryMetricsProvider = memoryMetricsProvider;
        _networkMetricsProvider = networkMetricsProvider;
        _processMetricsProvider = processMetricsProvider;
    }

    public event EventHandler<CpuSample>? CpuSampleReceived;
    public event EventHandler<MemorySample>? MemorySampleReceived;
    public event EventHandler<NetworkSample>? NetworkSampleReceived;
    public event EventHandler<ProcessSample>? ProcessSampleReceived;
    public event EventHandler<Exception>? PollerFaulted;

    public async Task StartAsync(TimeSpan interval, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_runTask is not null)
            {
                return;
            }

            _internalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runTask = RunAsync(interval, _internalCts.Token);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_internalCts is null || _runTask is null)
            {
                return;
            }

            _internalCts.Cancel();
            try
            {
                await _runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _internalCts.Dispose();
                _internalCts = null;
                _runTask = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();

        if (_cpuMetricsProvider is IDisposable cpuDisposable)
        {
            cpuDisposable.Dispose();
        }

        if (_memoryMetricsProvider is IDisposable memoryDisposable)
        {
            memoryDisposable.Dispose();
        }

        if (_networkMetricsProvider is IDisposable networkDisposable)
        {
            networkDisposable.Dispose();
        }

        if (_processMetricsProvider is IDisposable processDisposable)
        {
            processDisposable.Dispose();
        }
    }

    private async Task RunAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                // Sampling runs on the poller background thread so UI rendering stays responsive.
                var cpu = await _cpuMetricsProvider.GetCpuSampleAsync(cancellationToken).ConfigureAwait(false);
                var memory = await _memoryMetricsProvider.GetMemorySampleAsync(cancellationToken).ConfigureAwait(false);
                var network = await _networkMetricsProvider.GetNetworkSampleAsync(cancellationToken).ConfigureAwait(false);
                var processes = await _processMetricsProvider.GetProcessSampleAsync(cancellationToken).ConfigureAwait(false);

                CpuSampleReceived?.Invoke(this, cpu);
                MemorySampleReceived?.Invoke(this, memory);
                NetworkSampleReceived?.Invoke(this, network);
                ProcessSampleReceived?.Invoke(this, processes);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                PollerFaulted?.Invoke(this, ex);
            }
        }
    }
}
