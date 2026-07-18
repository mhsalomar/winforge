using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WinForge.Core;
using WinForge.Infrastructure;
using WinForge.Localization;
using Wpf.Ui.Appearance;

namespace WinForge.UI;

public partial class App : Application
{
    private ServiceProvider? _services;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        var services = new ServiceCollection();
        services.AddSingleton<Localizer>().AddSingleton<SafeProcessRunner>()
            .AddSingleton<IStartupReader, StartupReader>().AddSingleton<IScheduledTaskReader, ScheduledTaskReader>()
            .AddSingleton<IServiceReader, ServiceReader>().AddSingleton<IAppxReader, AppxReader>()
            .AddSingleton<IOptionalFeatureReader, OptionalFeatureReader>().AddSingleton<INetworkReader, NetworkReader>()
            .AddSingleton<ISystemScanner, WindowsSystemScanner>().AddSingleton<SafeCleaner>()
            .AddSingleton<ICleaner>(sp => sp.GetRequiredService<SafeCleaner>()).AddSingleton<JournalStore>()
            .AddSingleton<IJournalStore>(sp => sp.GetRequiredService<JournalStore>()).AddSingleton<ISupportBundleCreator, SupportBundleCreator>()
            .AddSingleton<CatalogRepository>().AddSingleton<PowerPlanManager>().AddSingleton<WorkerClient>().AddSingleton<MainViewModel>().AddSingleton<MainWindow>();
        _services = services.BuildServiceProvider();
        _services.GetRequiredService<MainWindow>().Show();
    }
    protected override void OnExit(ExitEventArgs e) { _services?.Dispose(); base.OnExit(e); }
}
