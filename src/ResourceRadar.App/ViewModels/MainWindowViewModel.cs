using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Threading;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using ResourceRadar.Monitoring.Abstractions;
using ResourceRadar.Monitoring.Models;
using SkiaSharp;

namespace ResourceRadar.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private const int HistoryLength = 60;

    private readonly ObservableCollection<double> _cpuHistory = new();
    private readonly ObservableCollection<double> _memoryHistory = new();
    private readonly ObservableCollection<double> _networkDownloadHistory = new();
    private readonly ObservableCollection<double> _networkUploadHistory = new();
    private readonly IMetricsPoller _poller;
    private readonly IProcessControlService _processControlService;

    private readonly ObservableCollection<ProcessRowViewModel> _processes = new();
    private List<ProcessMetrics> _latestProcesses = [];

    private ProcessSortColumn _processSortColumn = ProcessSortColumn.Cpu;
    private bool _processSortAscending;

    private double _cpuUsagePercent;
    private double _memoryUsagePercent;
    private double _downloadBytesPerSecond;
    private double _uploadBytesPerSecond;
    private ulong _totalBytesSent;
    private ulong _totalBytesReceived;
    private int _activeInterfaceCount;

    private string _totalRamText = "Total: -";
    private string _usedRamText = "Used: -";
    private string _availableRamText = "Available: -";
    private string _statusMessage = string.Empty;

    public MainWindowViewModel(IMetricsPoller poller, IProcessControlService processControlService)
    {
        _poller = poller;
        _processControlService = processControlService;
        _processSortAscending = false;

        for (var i = 0; i < HistoryLength; i++)
        {
            _cpuHistory.Add(0);
            _memoryHistory.Add(0);
            _networkDownloadHistory.Add(0);
            _networkUploadHistory.Add(0);
        }

        CpuSeries =
        [
            new LineSeries<double>
            {
                Values = _cpuHistory,
                GeometrySize = 0,
                Fill = null,
                Name = "CPU",
                Stroke = new SolidColorPaint(new SKColor(56, 139, 253), 3)
            }
        ];

        MemorySeries =
        [
            new LineSeries<double>
            {
                Values = _memoryHistory,
                GeometrySize = 0,
                Fill = null,
                Name = "Memory",
                Stroke = new SolidColorPaint(new SKColor(35, 134, 54), 3)
            }
        ];

        NetworkSeries =
        [
            new LineSeries<double>
            {
                Values = _networkDownloadHistory,
                GeometrySize = 0,
                Fill = null,
                Name = "Download",
                Stroke = new SolidColorPaint(new SKColor(56, 139, 253), 3)
            },
            new LineSeries<double>
            {
                Values = _networkUploadHistory,
                GeometrySize = 0,
                Fill = null,
                Name = "Upload",
                Stroke = new SolidColorPaint(new SKColor(248, 129, 47), 3)
            }
        ];

        var separatorPaint = new SolidColorPaint(new SKColor(48, 54, 61), 1);
        var labelPaint = new SolidColorPaint(new SKColor(201, 209, 217), 1);

        CpuXAxes =
        [
            new Axis
            {
                IsVisible = false,
                MinLimit = 0,
                MaxLimit = HistoryLength - 1
            }
        ];

        CpuYAxes =
        [
            new Axis
            {
                MinLimit = 0,
                MaxLimit = 100,
                Labeler = value => $"{value:0}%",
                LabelsPaint = labelPaint,
                SeparatorsPaint = separatorPaint
            }
        ];

        MemoryXAxes =
        [
            new Axis
            {
                IsVisible = false,
                MinLimit = 0,
                MaxLimit = HistoryLength - 1
            }
        ];

        MemoryYAxes =
        [
            new Axis
            {
                MinLimit = 0,
                MaxLimit = 100,
                Labeler = value => $"{value:0}%",
                LabelsPaint = labelPaint,
                SeparatorsPaint = separatorPaint
            }
        ];

        NetworkXAxes =
        [
            new Axis
            {
                IsVisible = false,
                MinLimit = 0,
                MaxLimit = HistoryLength - 1
            }
        ];

        NetworkYAxes =
        [
            new Axis
            {
                MinLimit = 0,
                Labeler = value => string.Format(CultureInfo.InvariantCulture, "{0:0.0} KB/s", value),
                LabelsPaint = labelPaint,
                SeparatorsPaint = separatorPaint
            }
        ];

        Processes = _processes;

        SortByNameCommand = new RelayCommand(_ => SetProcessSort(ProcessSortColumn.Name));
        SortByCpuCommand = new RelayCommand(_ => SetProcessSort(ProcessSortColumn.Cpu));
        SortByMemoryCommand = new RelayCommand(_ => SetProcessSort(ProcessSortColumn.Memory));
        SortByTrafficCommand = new RelayCommand(_ => SetProcessSort(ProcessSortColumn.Traffic));
        EndProcessCommand = new RelayCommand(EndProcess);

        _poller.CpuSampleReceived += OnCpuSampleReceived;
        _poller.MemorySampleReceived += OnMemorySampleReceived;
        _poller.NetworkSampleReceived += OnNetworkSampleReceived;
        _poller.ProcessSampleReceived += OnProcessSampleReceived;
        _poller.PollerFaulted += OnPollerFaulted;

        _ = _poller.StartAsync(TimeSpan.FromSeconds(1));
    }

    public ISeries[] CpuSeries { get; }
    public ISeries[] MemorySeries { get; }
    public ISeries[] NetworkSeries { get; }
    public Axis[] CpuXAxes { get; }
    public Axis[] CpuYAxes { get; }
    public Axis[] MemoryXAxes { get; }
    public Axis[] MemoryYAxes { get; }
    public Axis[] NetworkXAxes { get; }
    public Axis[] NetworkYAxes { get; }

    public ObservableCollection<ProcessRowViewModel> Processes { get; }

    public ICommand SortByNameCommand { get; }
    public ICommand SortByCpuCommand { get; }
    public ICommand SortByMemoryCommand { get; }
    public ICommand SortByTrafficCommand { get; }
    public ICommand EndProcessCommand { get; }

    public string CpuUsageText => $"{_cpuUsagePercent:0.0}%";

    public double MemoryUsagePercent
    {
        get => _memoryUsagePercent;
        private set
        {
            if (SetProperty(ref _memoryUsagePercent, value))
            {
                RaisePropertyChanged(nameof(MemoryUsageText));
            }
        }
    }

    public string MemoryUsageText => $"{MemoryUsagePercent:0.0}%";

    public string MemoryBreakdownText => $"{_usedRamText}  {_availableRamText}  {_totalRamText}";

    public string NetworkSpeedText =>
        string.Format(
            CultureInfo.InvariantCulture,
            "Down: {0}  Up: {1}",
            FormatRate(_downloadBytesPerSecond),
            FormatRate(_uploadBytesPerSecond));

    public string NetworkTotalsText =>
        string.Format(
            CultureInfo.InvariantCulture,
            "Received: {0}  Sent: {1}",
            FormatBytes(_totalBytesReceived),
            FormatBytes(_totalBytesSent));

    public string NetworkInterfacesText =>
        string.Format(CultureInfo.InvariantCulture, "{0} active interface(s)", _activeInterfaceCount);

    public string ProcessSummaryText =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0} processes, sorted by {1}",
            Processes.Count,
            GetSortDescription());

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                RaisePropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public void Dispose()
    {
        _poller.CpuSampleReceived -= OnCpuSampleReceived;
        _poller.MemorySampleReceived -= OnMemorySampleReceived;
        _poller.NetworkSampleReceived -= OnNetworkSampleReceived;
        _poller.ProcessSampleReceived -= OnProcessSampleReceived;
        _poller.PollerFaulted -= OnPollerFaulted;

        _poller.StopAsync().GetAwaiter().GetResult();
        _poller.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private void OnCpuSampleReceived(object? sender, CpuSample sample)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // The chart uses a fixed-size rolling window to keep rendering work predictable.
            _cpuUsagePercent = sample.TotalUsagePercent;
            RaisePropertyChanged(nameof(CpuUsageText));
            AppendHistoryPoint(_cpuHistory, sample.TotalUsagePercent);
        });
    }

    private void OnMemorySampleReceived(object? sender, MemorySample sample)
    {
        Dispatcher.UIThread.Post(() =>
        {
            MemoryUsagePercent = sample.UsedPercent;
            _totalRamText = $"Total: {FormatBytes(sample.TotalBytes)}";
            _usedRamText = $"Used: {FormatBytes(sample.UsedBytes)}";
            _availableRamText = $"Available: {FormatBytes(sample.AvailableBytes)}";

            RaisePropertyChanged(nameof(MemoryBreakdownText));
            AppendHistoryPoint(_memoryHistory, sample.UsedPercent);
        });
    }

    private void OnNetworkSampleReceived(object? sender, NetworkSample sample)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _downloadBytesPerSecond = sample.DownloadBytesPerSecond;
            _uploadBytesPerSecond = sample.UploadBytesPerSecond;
            _totalBytesReceived = sample.TotalBytesReceived;
            _totalBytesSent = sample.TotalBytesSent;
            _activeInterfaceCount = sample.ActiveInterfaceCount;

            AppendHistoryPoint(_networkDownloadHistory, sample.DownloadBytesPerSecond / 1024.0);
            AppendHistoryPoint(_networkUploadHistory, sample.UploadBytesPerSecond / 1024.0);

            RaisePropertyChanged(nameof(NetworkSpeedText));
            RaisePropertyChanged(nameof(NetworkTotalsText));
            RaisePropertyChanged(nameof(NetworkInterfacesText));
        });
    }

    private void OnProcessSampleReceived(object? sender, ProcessSample sample)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _latestProcesses = sample.Processes.ToList();
            ApplyProcessSorting();
        });
    }

    private void OnPollerFaulted(object? sender, Exception exception)
    {
        Dispatcher.UIThread.Post(() =>
        {
            StatusMessage = $"Telemetry error: {exception.Message}";
        });
    }

    private void EndProcess(object? parameter)
    {
        if (!TryParseProcessId(parameter, out var processId))
        {
            return;
        }

        if (_processControlService.TryTerminateProcess(processId, out var errorMessage))
        {
            StatusMessage = string.Format(CultureInfo.InvariantCulture, "Termination requested for PID {0}.", processId);
            _latestProcesses = _latestProcesses
                .Where(process => process.ProcessId != processId)
                .ToList();
            ApplyProcessSorting();
            return;
        }

        StatusMessage = string.Format(
            CultureInfo.InvariantCulture,
            "Failed to terminate PID {0}: {1}",
            processId,
            string.IsNullOrWhiteSpace(errorMessage) ? "unknown error" : errorMessage);
    }

    private void SetProcessSort(ProcessSortColumn sortColumn)
    {
        if (_processSortColumn == sortColumn)
        {
            _processSortAscending = !_processSortAscending;
        }
        else
        {
            _processSortColumn = sortColumn;
            _processSortAscending = sortColumn == ProcessSortColumn.Name;
        }

        ApplyProcessSorting();
    }

    private void ApplyProcessSorting()
    {
        IEnumerable<ProcessMetrics> ordered = _processSortColumn switch
        {
            ProcessSortColumn.Name => _processSortAscending
                ? _latestProcesses.OrderBy(static process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
                : _latestProcesses.OrderByDescending(static process => process.ProcessName, StringComparer.OrdinalIgnoreCase),
            ProcessSortColumn.Memory => _processSortAscending
                ? _latestProcesses.OrderBy(static process => process.MemoryBytes)
                : _latestProcesses.OrderByDescending(static process => process.MemoryBytes),
            ProcessSortColumn.Traffic => _processSortAscending
                ? _latestProcesses.OrderBy(static process => process.TrafficConnectionCount)
                : _latestProcesses.OrderByDescending(static process => process.TrafficConnectionCount),
            _ => _processSortAscending
                ? _latestProcesses.OrderBy(static process => process.CpuPercent)
                : _latestProcesses.OrderByDescending(static process => process.CpuPercent)
        };

        var rows = ordered
            .Select(static process => new ProcessRowViewModel
            {
                ProcessId = process.ProcessId,
                ProcessName = process.ProcessName,
                CpuPercent = process.CpuPercent,
                MemoryBytes = process.MemoryBytes,
                TrafficConnectionCount = process.TrafficConnectionCount
            })
            .ToList();

        _processes.Clear();
        foreach (var row in rows)
        {
            _processes.Add(row);
        }

        RaisePropertyChanged(nameof(ProcessSummaryText));
    }

    private string GetSortDescription()
    {
        var direction = _processSortAscending ? "ascending" : "descending";
        var column = _processSortColumn switch
        {
            ProcessSortColumn.Name => "name",
            ProcessSortColumn.Memory => "memory",
            ProcessSortColumn.Traffic => "traffic (connections)",
            _ => "CPU"
        };

        return string.Format(CultureInfo.InvariantCulture, "{0} ({1})", column, direction);
    }

    private static bool TryParseProcessId(object? parameter, out int processId)
    {
        processId = 0;

        return parameter switch
        {
            int id => (processId = id) > 0,
            long id => id > 0 && id <= int.MaxValue && (processId = (int)id) > 0,
            string text => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out processId) && processId > 0,
            _ => false
        };
    }

    private static void AppendHistoryPoint(ObservableCollection<double> history, double value)
    {
        history.Add(value);
        while (history.Count > HistoryLength)
        {
            history.RemoveAt(0);
        }
    }

    private static string FormatBytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:0.0} {1}", value, units[unitIndex]);
    }

    private static string FormatRate(double bytesPerSecond)
    {
        string[] units = ["B/s", "KB/s", "MB/s", "GB/s"];
        var value = bytesPerSecond;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:0.0} {1}", value, units[unitIndex]);
    }

    private enum ProcessSortColumn
    {
        Name,
        Cpu,
        Memory,
        Traffic
    }
}
