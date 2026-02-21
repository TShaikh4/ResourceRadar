using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using ResourceRadar.Monitoring.Abstractions;
using ResourceRadar.Monitoring.Models;

namespace ResourceRadar.Monitoring.Providers;

public sealed partial class MacOsMemoryMetricsProvider : IMemoryMetricsProvider
{
    private readonly ulong _totalBytes;

    public MacOsMemoryMetricsProvider()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("macOS memory provider can only run on macOS.");
        }

        var totalOutput = RunCommand("/usr/sbin/sysctl", "-n hw.memsize");
        _totalBytes = ulong.TryParse(totalOutput.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var total)
            ? total
            : 0;
    }

    public ValueTask<MemorySample> GetMemorySampleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // vm_stat exposes free/inactive/speculative pages; combining them gives usable memory.
        var vmStat = RunCommand("/usr/bin/vm_stat", string.Empty);

        var pageSizeMatch = PageSizeRegex().Match(vmStat);
        var pageSize = pageSizeMatch.Success
            ? ParseUlong(pageSizeMatch.Groups["size"].Value)
            : 4096;

        var freePages = FindPageCount(vmStat, "Pages free");
        var inactivePages = FindPageCount(vmStat, "Pages inactive");
        var speculativePages = FindPageCount(vmStat, "Pages speculative");

        var availablePages = freePages + inactivePages + speculativePages;
        var availableBytes = pageSize * availablePages;
        if (_totalBytes > 0)
        {
            availableBytes = Math.Min(availableBytes, _totalBytes);
        }

        var usedBytes = _totalBytes >= availableBytes ? _totalBytes - availableBytes : 0;

        return ValueTask.FromResult(new MemorySample(
            TotalBytes: _totalBytes,
            UsedBytes: usedBytes,
            AvailableBytes: availableBytes,
            Timestamp: DateTimeOffset.UtcNow));
    }

    private static ulong FindPageCount(string vmStatOutput, string key)
    {
        var match = Regex.Match(vmStatOutput, $@"{Regex.Escape(key)}:\s*(?<count>[0-9.]+)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return 0;
        }

        var normalized = match.Groups["count"].Value.Replace(".", string.Empty, StringComparison.Ordinal);
        return ParseUlong(normalized);
    }

    private static string RunCommand(string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Command failed with exit code {process.ExitCode}: {error}");
        }

        return output;
    }

    private static ulong ParseUlong(string value)
    {
        return ulong.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    [GeneratedRegex(@"page size of (?<size>[0-9]+) bytes", RegexOptions.IgnoreCase)]
    private static partial Regex PageSizeRegex();
}
