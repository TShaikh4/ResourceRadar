# ResourceRadar

ResourceRadar is a cross-platform desktop system monitor built with .NET 8 and Avalonia.
It provides real-time CPU, memory, network, and process telemetry in a single dashboard.

## Why this project

This project demonstrates practical desktop engineering patterns for production-style apps:

- Cross-platform UI with Avalonia (Windows, macOS, Linux)
- Clean separation between presentation and monitoring logic
- Event-driven, non-blocking telemetry pipeline
- MVVM binding with rolling real-time charts
- OS-specific metric providers behind stable interfaces

## Demo scope (current)

- CPU monitor
  - Live total CPU percentage
  - 60-point history chart
- Memory monitor
  - Total/used/available memory
  - Percentage progress bar
  - 60-point history chart
- Network monitor
  - Live download/upload speeds
  - Total bytes received/sent
  - Active interface count
  - 60-point dual-series chart (download + upload)
- Process list
  - Running processes with PID, CPU, memory, and traffic indicator
  - Sort by Name, CPU, Memory, or Traffic
  - End process action

Traffic sorting is currently based on active network connection count per process (not per-process bandwidth).

## Tech stack

- .NET 8 (`net8.0`)
- Avalonia UI 11.1.3
- LiveCharts2 (`LiveChartsCore`, `LiveChartsCore.SkiaSharpView.Avalonia`)
- `System.Diagnostics` / OS-native commands and files for metrics

## Project layout

- `src/ResourceRadar.App`
  - Avalonia desktop app
  - MVVM view models and XAML UI
- `src/ResourceRadar.Monitoring`
  - Monitoring abstractions, models, providers, and poller
- `ResourceRadar.sln`
  - Solution entrypoint
- `architecture.md`
  - Detailed architecture and data flow

## Architecture summary

ResourceRadar uses a two-project architecture:

- `ResourceRadar.App` handles rendering, user interactions, and view state.
- `ResourceRadar.Monitoring` handles metric collection and process actions.

`MetricsPoller` runs on a background timer and emits events for CPU, memory, network, and process samples. `MainWindowViewModel` receives these events, marshals updates to the UI thread, and updates chart/history collections.

For a detailed breakdown, see [architecture.md](architecture.md).

## Getting started

### Prerequisites

- .NET 8 SDK
  - Verify with: `dotnet --version`
  - Expected major: `8.x`

### Build and run

```bash
dotnet restore ResourceRadar.sln
dotnet build ResourceRadar.sln
dotnet run --project src/ResourceRadar.App/ResourceRadar.App.csproj
```

## How it works

### Polling and update cadence

- Poll interval: 1 second
- Chart history window: 60 points
- Sampling and calculations happen off the UI thread
- UI updates are posted through Avalonia dispatcher

### Metric collection strategy

CPU and memory use OS-specific providers:

- Windows
  - CPU: `PerformanceCounter` (Processor `% Processor Time`)
  - Memory: `GlobalMemoryStatusEx`
- Linux
  - CPU: `/proc/stat` delta snapshots
  - Memory: `/proc/meminfo`
- macOS
  - CPU: `top -l 1 -n 0`
  - Memory: `sysctl hw.memsize` + `vm_stat`

Network and process metrics are collected with cross-platform APIs plus platform-aware command fallbacks where needed.

## Design choices

- Monitoring in a separate class library:
  - Keeps UI code focused on rendering
  - Makes telemetry logic testable and reusable
- Interface-driven providers:
  - Easier to swap implementations by OS or future requirements
- Event-driven poller:
  - Prevents UI lockups and centralizes periodic sampling

## Known limitations

- Process “Traffic” currently uses connection count as a proxy, not exact bytes/sec per process.
- Some process/network details depend on OS permissions and available system APIs.
- Windows CPU provider may emit CA1416 analyzer warnings on non-Windows builds due to platform-specific APIs.

## Roadmap ideas

- Per-core CPU visualization
- True per-process network throughput (bytes/sec)
- Disk I/O throughput and storage sensors
- Historical export and snapshot persistence
- Unit/integration test suite for parser/provider behavior


