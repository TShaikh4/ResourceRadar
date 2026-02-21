using System.ComponentModel;
using System.Runtime.InteropServices;
using ResourceRadar.Monitoring.Abstractions;
using ResourceRadar.Monitoring.Models;

namespace ResourceRadar.Monitoring.Providers;

public sealed class WindowsMemoryMetricsProvider : IMemoryMetricsProvider
{
    public ValueTask<MemorySample> GetMemorySampleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows memory provider can only run on Windows.");
        }

        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to query memory status.");
        }

        var total = status.TotalPhysicalMemory;
        var available = Math.Min(status.AvailablePhysicalMemory, total);
        var used = total >= available ? total - available : 0;

        return ValueTask.FromResult(new MemorySample(
            TotalBytes: total,
            UsedBytes: used,
            AvailableBytes: available,
            Timestamp: DateTimeOffset.UtcNow));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx lpBuffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysicalMemory;
        public ulong AvailablePhysicalMemory;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;

        public MemoryStatusEx()
        {
            Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
        }
    }
}
