using ResourceRadar.Monitoring.Abstractions;
using ResourceRadar.Monitoring.Providers;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ICpuMetricsProvider>(_ => MetricsProviderFactory.CreateCpuProvider());
builder.Services.AddSingleton<IMemoryMetricsProvider>(_ => MetricsProviderFactory.CreateMemoryProvider());
builder.Services.AddSingleton<INetworkMetricsProvider>(_ => MetricsProviderFactory.CreateNetworkProvider());
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ResourceRadar API",
        Version = "v1",
        Description = "Local API exposing health and current telemetry snapshots."
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "ResourceRadar API v1");
    options.RoutePrefix = "swagger";
});

app.MapGet("/", () => Results.Redirect("/swagger"));

app.MapGet("/health", () =>
{
    return Results.Ok(new HealthResponse(
        Status: "ok",
        Service: "ResourceRadar.Api",
        TimestampUtc: DateTimeOffset.UtcNow));
})
.WithName("GetHealth")
.WithTags("System");

app.MapGet("/metrics/current", async (
    ICpuMetricsProvider cpuProvider,
    IMemoryMetricsProvider memoryProvider,
    INetworkMetricsProvider networkProvider,
    CancellationToken cancellationToken) =>
{
    // Fetching metrics concurrently keeps endpoint latency low while reusing provider internals.
    var cpuTask = cpuProvider.GetCpuSampleAsync(cancellationToken).AsTask();
    var memoryTask = memoryProvider.GetMemorySampleAsync(cancellationToken).AsTask();
    var networkTask = networkProvider.GetNetworkSampleAsync(cancellationToken).AsTask();

    await Task.WhenAll(cpuTask, memoryTask, networkTask);

    var cpuSample = await cpuTask;
    var memorySample = await memoryTask;
    var networkSample = await networkTask;

    var timestampUtc = new[]
    {
        cpuSample.Timestamp,
        memorySample.Timestamp,
        networkSample.Timestamp
    }.Max();

    return Results.Ok(new CurrentMetricsResponse(
        TimestampUtc: timestampUtc,
        Cpu: new CpuPayload(
            TotalUsagePercent: Math.Round(cpuSample.TotalUsagePercent, 2),
            PerCoreUsagePercent: cpuSample.PerCoreUsagePercent
                .Select(static value => Math.Round(value, 2))
                .ToArray()),
        Memory: new MemoryPayload(
            TotalBytes: memorySample.TotalBytes,
            UsedBytes: memorySample.UsedBytes,
            AvailableBytes: memorySample.AvailableBytes,
            UsedPercent: Math.Round(memorySample.UsedPercent, 2)),
        Network: new NetworkPayload(
            TotalBytesSent: networkSample.TotalBytesSent,
            TotalBytesReceived: networkSample.TotalBytesReceived,
            UploadBytesPerSecond: Math.Round(networkSample.UploadBytesPerSecond, 2),
            DownloadBytesPerSecond: Math.Round(networkSample.DownloadBytesPerSecond, 2),
            ActiveInterfaceCount: networkSample.ActiveInterfaceCount)));
})
.WithName("GetCurrentMetrics")
.WithTags("Metrics");

app.Run();

public sealed record HealthResponse(string Status, string Service, DateTimeOffset TimestampUtc);

public sealed record CurrentMetricsResponse(
    DateTimeOffset TimestampUtc,
    CpuPayload Cpu,
    MemoryPayload Memory,
    NetworkPayload Network);

public sealed record CpuPayload(
    double TotalUsagePercent,
    IReadOnlyList<double> PerCoreUsagePercent);

public sealed record MemoryPayload(
    ulong TotalBytes,
    ulong UsedBytes,
    ulong AvailableBytes,
    double UsedPercent);

public sealed record NetworkPayload(
    ulong TotalBytesSent,
    ulong TotalBytesReceived,
    double UploadBytesPerSecond,
    double DownloadBytesPerSecond,
    int ActiveInterfaceCount);
