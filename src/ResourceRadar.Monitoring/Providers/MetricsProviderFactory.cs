using System.Runtime.InteropServices;
using ResourceRadar.Monitoring.Abstractions;

namespace ResourceRadar.Monitoring.Providers;

public static class MetricsProviderFactory
{
    public static ICpuMetricsProvider CreateCpuProvider()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsCpuMetricsProvider();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxCpuMetricsProvider();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacOsCpuMetricsProvider();
        }

        throw new PlatformNotSupportedException("ResourceRadar supports Windows, Linux, and macOS.");
    }

    public static IMemoryMetricsProvider CreateMemoryProvider()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsMemoryMetricsProvider();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxMemoryMetricsProvider();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacOsMemoryMetricsProvider();
        }

        throw new PlatformNotSupportedException("ResourceRadar supports Windows, Linux, and macOS.");
    }

    public static INetworkMetricsProvider CreateNetworkProvider()
    {
        return new NetworkMetricsProvider();
    }

    public static IProcessMetricsProvider CreateProcessProvider()
    {
        return new ProcessMetricsProvider();
    }

    public static IProcessControlService CreateProcessControlService()
    {
        return new ProcessControlService();
    }
}
