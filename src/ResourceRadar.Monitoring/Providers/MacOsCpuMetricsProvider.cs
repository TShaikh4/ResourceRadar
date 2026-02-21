using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using ResourceRadar.Monitoring.Abstractions;
using ResourceRadar.Monitoring.Models;

namespace ResourceRadar.Monitoring.Providers;

public sealed partial class MacOsCpuMetricsProvider : ICpuMetricsProvider
{
    private readonly int _coreCount = Math.Max(Environment.ProcessorCount, 1);

    public async ValueTask<CpuSample> GetCpuSampleAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("macOS CPU provider can only run on macOS.");
        }

        // top reports system-wide CPU usage percentages on macOS without elevated privileges.
        var output = await RunCommandAsync("/usr/bin/top", "-l 1 -n 0", cancellationToken).ConfigureAwait(false);
        var match = CpuUsageRegex().Match(output);

        if (!match.Success)
        {
            throw new InvalidOperationException("Unable to parse CPU usage from top output.");
        }

        var user = ParseDouble(match.Groups["user"].Value);
        var system = ParseDouble(match.Groups["system"].Value);
        var total = Math.Clamp(user + system, 0, 100);

        var perCore = Enumerable.Repeat(total, _coreCount).ToArray();

        return new CpuSample(
            TotalUsagePercent: total,
            PerCoreUsagePercent: perCore,
            Timestamp: DateTimeOffset.UtcNow);
    }

    private static async Task<string> RunCommandAsync(string fileName, string arguments, CancellationToken cancellationToken)
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

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Command failed with exit code {process.ExitCode}: {error}");
        }

        return output;
    }

    private static double ParseDouble(string value)
    {
        return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"CPU usage:\s*(?<user>[0-9.]+)% user,\s*(?<system>[0-9.]+)% sys,\s*(?<idle>[0-9.]+)% idle", RegexOptions.IgnoreCase)]
    private static partial Regex CpuUsageRegex();
}
