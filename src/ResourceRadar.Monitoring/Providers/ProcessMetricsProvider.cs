using System.Diagnostics;
using ResourceRadar.Monitoring.Abstractions;
using ResourceRadar.Monitoring.Models;

namespace ResourceRadar.Monitoring.Providers;

public sealed class ProcessMetricsProvider : IProcessMetricsProvider
{
    private static readonly TimeSpan ConnectionCountRefreshInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ProcessNameRefreshInterval = TimeSpan.FromSeconds(10);

    private readonly Dictionary<int, TimeSpan> _previousCpuByPid = new();
    private Dictionary<int, int> _cachedConnectionCountsByPid = [];
    private DateTimeOffset _cachedConnectionCountsTimestamp = DateTimeOffset.MinValue;
    private Dictionary<int, string> _cachedDisplayNamesByPid = [];
    private DateTimeOffset _cachedDisplayNamesTimestamp = DateTimeOffset.MinValue;
    private DateTimeOffset? _previousTimestamp;

    public ValueTask<ProcessSample> GetProcessSampleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTimeOffset.UtcNow;
        var connectionCountsByPid = GetConnectionCounts(now);
        var displayNamesByPid = GetProcessDisplayNames(now);
        var elapsed = _previousTimestamp.HasValue
            ? now - _previousTimestamp.Value
            : TimeSpan.Zero;

        var currentCpuByPid = new Dictionary<int, TimeSpan>();
        var processes = new List<ProcessMetrics>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (!TryReadProcess(process, out var pid, out var name, out var cpuTime, out var memoryBytes))
                {
                    continue;
                }

                if (displayNamesByPid.TryGetValue(pid, out var displayName) && !string.IsNullOrWhiteSpace(displayName))
                {
                    name = displayName;
                }

                var cpuPercent = 0.0;

                // CPU% is calculated from per-process CPU deltas normalized by elapsed wall time and core count.
                if (elapsed > TimeSpan.Zero && _previousCpuByPid.TryGetValue(pid, out var previousCpu))
                {
                    var deltaCpu = cpuTime - previousCpu;
                    if (deltaCpu > TimeSpan.Zero)
                    {
                        var normalized = deltaCpu.TotalMilliseconds / (elapsed.TotalMilliseconds * Environment.ProcessorCount);
                        cpuPercent = Math.Clamp(normalized * 100.0, 0, 100);
                    }
                }

                currentCpuByPid[pid] = cpuTime;
                connectionCountsByPid.TryGetValue(pid, out var connectionCount);
                processes.Add(new ProcessMetrics(
                    ProcessId: pid,
                    ProcessName: name,
                    CpuPercent: cpuPercent,
                    MemoryBytes: memoryBytes,
                    TrafficConnectionCount: connectionCount));
            }
        }

        _previousCpuByPid.Clear();
        foreach (var pair in currentCpuByPid)
        {
            _previousCpuByPid[pair.Key] = pair.Value;
        }

        _previousTimestamp = now;

        return ValueTask.FromResult(new ProcessSample(processes, now));
    }

    private IReadOnlyDictionary<int, int> GetConnectionCounts(DateTimeOffset now)
    {
        if ((now - _cachedConnectionCountsTimestamp) < ConnectionCountRefreshInterval)
        {
            return _cachedConnectionCountsByPid;
        }

        _cachedConnectionCountsByPid = OperatingSystem.IsWindows()
            ? ReadWindowsConnectionCounts()
            : ReadUnixConnectionCounts();
        _cachedConnectionCountsTimestamp = now;
        return _cachedConnectionCountsByPid;
    }

    private IReadOnlyDictionary<int, string> GetProcessDisplayNames(DateTimeOffset now)
    {
        if (OperatingSystem.IsWindows())
        {
            return _cachedDisplayNamesByPid;
        }

        if ((now - _cachedDisplayNamesTimestamp) < ProcessNameRefreshInterval)
        {
            return _cachedDisplayNamesByPid;
        }

        _cachedDisplayNamesByPid = ReadUnixDisplayNames();
        _cachedDisplayNamesTimestamp = now;
        return _cachedDisplayNamesByPid;
    }

    private static Dictionary<int, int> ReadUnixConnectionCounts()
    {
        // lsof returns network handles grouped by process; counting those handles gives a traffic activity proxy.
        var output = RunCommand("lsof", "-nP -i -F pf");
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var counts = new Dictionary<int, int>();
        var currentPid = -1;

        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawLine.Length < 2)
            {
                continue;
            }

            if (rawLine[0] == 'p')
            {
                currentPid = int.TryParse(rawLine.AsSpan(1), out var parsedPid) ? parsedPid : -1;
                continue;
            }

            if (rawLine[0] == 'f' && currentPid > 0)
            {
                counts[currentPid] = counts.TryGetValue(currentPid, out var existing)
                    ? existing + 1
                    : 1;
            }
        }

        return counts;
    }

    private static Dictionary<int, int> ReadWindowsConnectionCounts()
    {
        var counts = new Dictionary<int, int>();
        ParseWindowsNetstatOutput(RunCommand("netstat", "-ano -p tcp"), counts);
        ParseWindowsNetstatOutput(RunCommand("netstat", "-ano -p udp"), counts);
        return counts;
    }

    private static Dictionary<int, string> ReadUnixDisplayNames()
    {
        var output = RunCommand("ps", "-axww -o pid=,command=");
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var namesByPid = new Dictionary<int, string>();

        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var splitIndex = rawLine.IndexOf(' ');
            if (splitIndex <= 0)
            {
                continue;
            }

            var pidText = rawLine[..splitIndex].Trim();
            if (!int.TryParse(pidText, out var pid) || pid <= 0)
            {
                continue;
            }

            var commandLine = rawLine[(splitIndex + 1)..].Trim();
            var displayName = DeriveDisplayNameFromCommandLine(commandLine);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                continue;
            }

            namesByPid[pid] = displayName;
        }

        return namesByPid;
    }

    private static void ParseWindowsNetstatOutput(string output, Dictionary<int, int> counts)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawLine.StartsWith("Proto", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = rawLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            var pidText = parts[^1];
            if (!int.TryParse(pidText, out var pid) || pid <= 0)
            {
                continue;
            }

            counts[pid] = counts.TryGetValue(pid, out var existing)
                ? existing + 1
                : 1;
        }
    }

    private static string RunCommand(string fileName, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = false,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(2000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                return string.Empty;
            }

            return process.ExitCode == 0
                ? outputTask.GetAwaiter().GetResult()
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string DeriveDisplayNameFromCommandLine(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return string.Empty;
        }

        var trimmed = commandLine.Trim();

        const string appMarker = ".app/Contents/MacOS/";
        var appMarkerIndex = trimmed.IndexOf(appMarker, StringComparison.OrdinalIgnoreCase);
        if (appMarkerIndex >= 0)
        {
            var executableSegment = trimmed[(appMarkerIndex + appMarker.Length)..];
            var argumentStart = executableSegment.IndexOf(" --", StringComparison.Ordinal);
            return argumentStart > 0
                ? executableSegment[..argumentStart].Trim()
                : executableSegment.Trim();
        }

        var firstArgument = ExtractFirstArgument(trimmed);
        if (string.IsNullOrWhiteSpace(firstArgument))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(firstArgument) || firstArgument.StartsWith("./", StringComparison.Ordinal))
        {
            var fromPath = Path.GetFileName(firstArgument);
            if (!string.IsNullOrWhiteSpace(fromPath))
            {
                return fromPath;
            }
        }

        return firstArgument;
    }

    private static string ExtractFirstArgument(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return string.Empty;
        }

        var inQuotes = false;
        var quoteChar = '\0';
        var escaped = false;
        var endIndex = commandLine.Length;

        for (var index = 0; index < commandLine.Length; index++)
        {
            var character = commandLine[index];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (character == '\\')
            {
                escaped = true;
                continue;
            }

            if (inQuotes)
            {
                if (character == quoteChar)
                {
                    inQuotes = false;
                }

                continue;
            }

            if (character is '"' or '\'')
            {
                inQuotes = true;
                quoteChar = character;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                endIndex = index;
                break;
            }
        }

        return commandLine[..endIndex].Trim('"', '\'');
    }

    private static bool TryReadProcess(
        Process process,
        out int processId,
        out string processName,
        out TimeSpan totalCpuTime,
        out ulong memoryBytes)
    {
        processId = 0;
        processName = string.Empty;
        totalCpuTime = TimeSpan.Zero;
        memoryBytes = 0;

        try
        {
            processId = process.Id;
            processName = process.ProcessName;
            totalCpuTime = process.TotalProcessorTime;
            memoryBytes = process.WorkingSet64 < 0 ? 0 : (ulong)process.WorkingSet64;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
