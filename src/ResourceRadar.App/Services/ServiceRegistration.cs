using Microsoft.Extensions.DependencyInjection;
using ResourceRadar.App.ViewModels;
using ResourceRadar.Monitoring;
using ResourceRadar.Monitoring.Abstractions;
using ResourceRadar.Monitoring.Providers;

namespace ResourceRadar.App.Services;

public static class ServiceRegistration
{
    public static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ICpuMetricsProvider>(_ => MetricsProviderFactory.CreateCpuProvider());
        services.AddSingleton<IMemoryMetricsProvider>(_ => MetricsProviderFactory.CreateMemoryProvider());
        services.AddSingleton<INetworkMetricsProvider>(_ => MetricsProviderFactory.CreateNetworkProvider());
        services.AddSingleton<IProcessMetricsProvider>(_ => MetricsProviderFactory.CreateProcessProvider());
        services.AddSingleton<IProcessControlService>(_ => MetricsProviderFactory.CreateProcessControlService());
        services.AddSingleton<IMetricsPoller, MetricsPoller>();

        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }
}
