using System.Runtime.InteropServices;
using ResourceRadar.Monitoring.Abstractions;
using ResourceRadar.Monitoring.Models;

namespace ResourceRadar.Monitoring.Providers;

public sealed class MacOsCpuMetricsProvider : ICpuMetricsProvider, IDisposable
{
    private const int KERN_SUCCESS = 0;
    private const int PROCESSOR_CPU_LOAD_INFO = 2;
    private const int CPU_STATE_MAX = 4;
    private const int CPU_STATE_USER = 0;
    private const int CPU_STATE_SYSTEM = 1;
    private const int CPU_STATE_IDLE = 2;
    private const int CPU_STATE_NICE = 3;
    private static readonly TimeSpan InitialSamplingWindow = TimeSpan.FromMilliseconds(150);

    private readonly SemaphoreSlim _sampleGate = new(1, 1);
    private CpuLoadSnapshot[] _previousSnapshots = [];

    public async ValueTask<CpuSample> GetCpuSampleAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("macOS CPU provider can only run on macOS.");
        }

        await _sampleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var timestamp = DateTimeOffset.UtcNow;
            var currentSnapshots = ReadCpuLoadSnapshots();

            if (currentSnapshots.Length == 0)
            {
                return new CpuSample(
                    TotalUsagePercent: 0,
                    PerCoreUsagePercent: [0],
                    Timestamp: timestamp);
            }

            if (_previousSnapshots.Length != currentSnapshots.Length)
            {
                // Take a short second sample so first readings are based on deltas, not boot-time totals.
                _previousSnapshots = currentSnapshots;
                await Task.Delay(InitialSamplingWindow, cancellationToken).ConfigureAwait(false);
                timestamp = DateTimeOffset.UtcNow;
                currentSnapshots = ReadCpuLoadSnapshots();
            }

            var previousSnapshots = _previousSnapshots;
            var perCore = new double[currentSnapshots.Length];
            double totalActiveDelta = 0;
            double totalTicksDelta = 0;

            for (var i = 0; i < currentSnapshots.Length; i++)
            {
                var current = currentSnapshots[i];
                var previous = i < previousSnapshots.Length ? previousSnapshots[i] : current;

                var ticksDelta = current.TotalTicks - previous.TotalTicks;
                var activeDelta = current.ActiveTicks - previous.ActiveTicks;

                if (ticksDelta <= 0)
                {
                    perCore[i] = 0;
                    continue;
                }

                var activeClamped = Math.Max(activeDelta, 0);
                var usage = activeClamped * 100.0 / ticksDelta;
                perCore[i] = Math.Clamp(usage, 0, 100);

                totalActiveDelta += activeClamped;
                totalTicksDelta += ticksDelta;
            }

            _previousSnapshots = currentSnapshots;

            var totalUsage = totalTicksDelta > 0
                ? Math.Clamp(totalActiveDelta * 100.0 / totalTicksDelta, 0, 100)
                : perCore.Average();

            return new CpuSample(
                TotalUsagePercent: totalUsage,
                PerCoreUsagePercent: perCore,
                Timestamp: timestamp);
        }
        finally
        {
            _sampleGate.Release();
        }
    }

    public void Dispose()
    {
        _sampleGate.Dispose();
    }

    private static CpuLoadSnapshot[] ReadCpuLoadSnapshots()
    {
        var task = mach_task_self();
        var host = mach_host_self();
        if (host == 0)
        {
            throw new InvalidOperationException("Unable to acquire macOS host port for CPU sampling.");
        }

        try
        {
            var result = host_processor_info(
                host,
                PROCESSOR_CPU_LOAD_INFO,
                out var processorCount,
                out var processorInfo,
                out var processorInfoCount);

            if (result != KERN_SUCCESS || processorCount == 0 || processorInfo == IntPtr.Zero)
            {
                if (processorInfo != IntPtr.Zero && processorInfoCount > 0)
                {
                    var invalidResponseSize = (nuint)(processorInfoCount * sizeof(int));
                    _ = vm_deallocate(task, processorInfo, invalidResponseSize);
                }

                return [];
            }

            try
            {
                var expectedInfoCount = processorCount * CPU_STATE_MAX;
                if (processorInfoCount < expectedInfoCount)
                {
                    return [];
                }

                var snapshots = new CpuLoadSnapshot[processorCount];
                for (var i = 0; i < processorCount; i++)
                {
                    var baseOffset = i * CPU_STATE_MAX * sizeof(int);
                    var user = ReadUInt32(processorInfo, baseOffset + (CPU_STATE_USER * sizeof(int)));
                    var system = ReadUInt32(processorInfo, baseOffset + (CPU_STATE_SYSTEM * sizeof(int)));
                    var idle = ReadUInt32(processorInfo, baseOffset + (CPU_STATE_IDLE * sizeof(int)));
                    var nice = ReadUInt32(processorInfo, baseOffset + (CPU_STATE_NICE * sizeof(int)));

                    var activeTicks = user + system + nice;
                    var totalTicks = activeTicks + idle;

                    snapshots[i] = new CpuLoadSnapshot(activeTicks, totalTicks);
                }

                return snapshots;
            }
            finally
            {
                var deallocateSize = (nuint)(processorInfoCount * sizeof(int));
                _ = vm_deallocate(task, processorInfo, deallocateSize);
            }
        }
        finally
        {
            _ = mach_port_deallocate(task, host);
        }
    }

    private static ulong ReadUInt32(IntPtr basePointer, int offset)
    {
        return unchecked((uint)Marshal.ReadInt32(basePointer, offset));
    }

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern uint mach_host_self();

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern uint mach_task_self();

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern int host_processor_info(
        uint host,
        int flavor,
        out int outProcessorCount,
        out IntPtr outProcessorInfo,
        out uint outProcessorInfoCount);

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern int vm_deallocate(
        uint targetTask,
        IntPtr address,
        nuint size);

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern int mach_port_deallocate(uint task, uint name);

    private readonly record struct CpuLoadSnapshot(ulong ActiveTicks, ulong TotalTicks);
}
