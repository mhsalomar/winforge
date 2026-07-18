using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using WinForge.Core;
using WinForge.Infrastructure;
using WinForge.Localization;
using Wpf.Ui.Appearance;

namespace WinForge.UI;

public sealed record NavigationItem(string Id, string Title, string Glyph, string Description);
public sealed record MetricRow(string Label, string Value);
public sealed record TweakDisplay(string Id, string Title, string Category, string Risk, string Recommendation, string Impact, string SideEffects, bool Admin, bool Restart, bool Reversible);
public sealed record CleanerDisplay(string Category, int Files, string Size, int Errors);
public sealed record RepairDisplay(string Tool, string Command, string Safety, string Restart);
public sealed record JournalDisplay(string SessionId, DateTime LastWrite, string Status);

public sealed class MainViewModel : ViewModelBase
{
    private readonly Localizer _localizer;
    private readonly ISystemScanner _scanner;
    private readonly SafeCleaner _cleaner;
    private readonly IStartupReader _startup;
    private readonly IServiceReader _services;
    private readonly IScheduledTaskReader _tasks;
    private readonly IAppxReader _apps;
    private readonly IOptionalFeatureReader _features;
    private readonly INetworkReader _network;
    private readonly ISupportBundleCreator _support;
    private readonly JournalStore _journal;
    private readonly CatalogRepository _catalogRepository;
    private readonly PowerPlanManager _power;
    private readonly WorkerClient _worker;
    private readonly ObservableCollection<object> _rows = [];
    private IReadOnlyList<CleanerPreview> _previews = [];
    private LoadedCatalog? _loadedCatalog;
    private SystemSnapshot? _snapshot;
    private Guid? _latestSessionId;
    private NavigationItem? _selectedNavigation;
    private string _status;
    private string _searchText = string.Empty;
    private bool _busy;
    private bool _dark = true;

    public MainViewModel(Localizer localizer, ISystemScanner scanner, SafeCleaner cleaner, IStartupReader startup, IServiceReader services,
        IScheduledTaskReader tasks, IAppxReader apps, IOptionalFeatureReader features, INetworkReader network, ISupportBundleCreator support,
        JournalStore journal, CatalogRepository catalogRepository, PowerPlanManager power, WorkerClient worker)
    {
        _localizer = localizer; _scanner = scanner; _cleaner = cleaner; _startup = startup; _services = services; _tasks = tasks; _apps = apps;
        _features = features; _network = network; _support = support; _journal = journal; _catalogRepository = catalogRepository; _power = power; _worker = worker;
        _status = localizer["Ready"];
        FilteredRows = CollectionViewSource.GetDefaultView(_rows); FilteredRows.Filter = FilterRow;
        PrimaryActionCommand = new AsyncCommand(RefreshCurrentAsync, () => !_busy);
        SecondaryActionCommand = new AsyncCommand(RunSecondaryActionAsync, () => !_busy && (SelectedNavigation?.Id is "privacy" or "gaming" || SelectedNavigation?.Id == "rollback" && _latestSessionId is not null || _previews.Count > 0));
        SupportBundleCommand = new AsyncCommand(CreateSupportBundleAsync, () => !_busy && _snapshot is not null);
        ToggleLanguageCommand = new RelayCommand(ToggleLanguage); ToggleThemeCommand = new RelayCommand(ToggleTheme);
        BuildNavigation();
    }

    public ObservableCollection<NavigationItem> Navigation { get; } = [];
    public ObservableCollection<MetricRow> Metrics { get; } = [];
    public ICollectionView FilteredRows { get; }
    public ICommand PrimaryActionCommand { get; }
    public ICommand SecondaryActionCommand { get; }
    public ICommand SupportBundleCommand { get; }
    public ICommand ToggleLanguageCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public NavigationItem? SelectedNavigation { get => _selectedNavigation; set { if (Set(ref _selectedNavigation, value) && value is not null) _ = RefreshCurrentAsync(); RaisePageProperties(); } }
    public string PageTitle => SelectedNavigation?.Title ?? _localizer["Dashboard"];
    public string PageDescription => SelectedNavigation?.Description ?? string.Empty;
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string SearchText { get => _searchText; set { if (Set(ref _searchText, value)) FilteredRows.Refresh(); } }
    public FlowDirection FlowDirection => _localizer.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    public Visibility BusyVisibility => _busy ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DashboardVisibility => SelectedNavigation?.Id == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AboutVisibility => SelectedNavigation?.Id is "about" or "settings" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TableVisibility => SelectedNavigation?.Id is "dashboard" or "about" or "settings" ? Visibility.Collapsed : Visibility.Visible;
    public Visibility SecondaryVisibility => SelectedNavigation?.Id is "privacy" or "gaming" || SelectedNavigation?.Id == "rollback" && _latestSessionId is not null || SelectedNavigation?.Id == "cleaner" && _previews.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string PrimaryActionLabel => SelectedNavigation?.Id switch { "dashboard" => _localizer["ScanSystem"], "cleaner" => _localizer["Preview"], _ => _localizer["Refresh"] };
    public string SecondaryActionLabel => SelectedNavigation?.Id switch { "privacy" or "gaming" => "Review & apply", "rollback" => "Restore latest session", _ => _localizer["CleanSelected"] };
    public string SupportBundleLabel => _localizer["SupportBundle"];

    public async Task InitializeAsync()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "catalog", "tweaks", "core.json");
            _loadedCatalog = await _catalogRepository.LoadAsync(path, _localizer.GetKnownKeys());
            if (Environment.GetEnvironmentVariable("WINFORGE_SKIP_STARTUP_SCAN") == "1")
            {
                _selectedNavigation = Navigation.FirstOrDefault(); Raise(nameof(SelectedNavigation)); RaisePageProperties();
            }
            else SelectedNavigation = Navigation.FirstOrDefault();
        }
        catch (Exception ex) { Status = $"Catalog error: {ex.Message}"; }
    }

    private async Task RefreshCurrentAsync()
    {
        if (_busy || SelectedNavigation is null) return;
        SetBusy(true);
        try
        {
            _rows.Clear(); Metrics.Clear();
            switch (SelectedNavigation.Id)
            {
                case "dashboard": await ScanAsync(); break;
                case "cleaner": await PreviewCleanerAsync(); break;
                case "startup": AddRows(await _startup.GetEntriesAsync()); break;
                case "services": AddRows(await _services.GetEntriesAsync()); break;
                case "tasks": AddRows(await _tasks.GetEntriesAsync()); break;
                case "apps": AddRows(await _apps.GetEntriesAsync()); break;
                case "features": AddRows(await _features.GetEntriesAsync()); break;
                case "network": AddRows(await _network.GetAdaptersAsync()); break;
                case "privacy": LoadTweaks("Privacy"); break;
                case "performance": LoadTweaks("Performance"); break;
                case "gaming": LoadTweaks("Gaming"); break;
                case "power": AddRows(await _power.GetSchemesAsync()); break;
                case "repair": LoadRepairTools(); break;
                case "rollback": LoadJournals(); break;
                case "logs": LoadLogs(); break;
            }
            Status = $"{PageTitle}: {_rows.Count} items loaded at {DateTime.Now:T}. Read-only scan completed.";
        }
        catch (Exception ex) { Status = $"{PageTitle}: {ex.Message}"; }
        finally { SetBusy(false); }
    }

    private async Task ScanAsync()
    {
        _snapshot = await _scanner.ScanAsync();
        Metrics.Add(new("Windows", $"{_snapshot.WindowsVersion} ({_snapshot.Edition}, {_snapshot.Build})")); Metrics.Add(new("Device", _snapshot.IsLaptop ? "Laptop" : "Desktop"));
        Metrics.Add(new("CPU", _snapshot.Cpu)); Metrics.Add(new("GPU", _snapshot.Gpu)); Metrics.Add(new("Memory", FormatBytes(_snapshot.MemoryBytes))); Metrics.Add(new("Power", _snapshot.ActivePowerScheme));
        Metrics.Add(new("Uptime", _snapshot.Uptime.ToString(@"d\.hh\:mm"))); Metrics.Add(new("Processes", _snapshot.ProcessCount.ToString())); Metrics.Add(new("Startup", _snapshot.StartupCount.ToString())); Metrics.Add(new("Scheduled tasks", _snapshot.ScheduledTaskCount.ToString()));
        foreach (var drive in _snapshot.Drives) Metrics.Add(new($"Drive {drive.Name}", $"{FormatBytes(drive.FreeBytes)} free / {FormatBytes(drive.TotalBytes)} · {drive.MediaType}"));
    }

    private async Task PreviewCleanerAsync()
    {
        var selected = _cleaner.Categories.Where(x => x.SelectedByDefault).Select(x => x.Id).ToArray();
        _previews = await _cleaner.PreviewAsync(selected);
        AddRows(_previews.Select(x => new CleanerDisplay(x.Category, x.FileCount, FormatBytes(x.TotalBytes), x.Errors.Count))); Raise(nameof(SecondaryVisibility));
    }

    private async Task CleanPreviewAsync()
    {
        if (_previews.Count == 0) return;
        if (MessageBox.Show($"Delete {_previews.Sum(x => x.FileCount)} previewed files? Locked files will be skipped.", "WinForge", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        SetBusy(true);
        try
        {
            var results = await _cleaner.CleanAsync(_previews); _rows.Clear(); AddRows(results);
            Status = $"Deleted {results.Sum(x => x.DeletedFiles)} files ({FormatBytes(results.Sum(x => x.DeletedBytes))}); {results.Sum(x => x.Errors.Count)} errors/skips."; _previews = [];
        }
        finally { SetBusy(false); Raise(nameof(SecondaryVisibility)); }
    }

    private Task RunSecondaryActionAsync() => SelectedNavigation?.Id switch { "privacy" or "gaming" => ApplyCurrentCategoryAsync(), "rollback" => RollbackLatestAsync(), _ => CleanPreviewAsync() };

    private async Task ApplyCurrentCategoryAsync()
    {
        if (_loadedCatalog is null || SelectedNavigation is null) return;
        _snapshot ??= await _scanner.ScanAsync();
        var category = SelectedNavigation.Id == "privacy" ? "Privacy" : "Gaming";
        var tweaks = _loadedCatalog.Catalog.Tweaks.Where(x => x.Category.Equals(category, StringComparison.OrdinalIgnoreCase) && x.Recommendation != Recommendation.NotRecommended).ToArray();
        if (tweaks.Length == 0) { Status = "No compatible catalog tweaks are available in this category."; return; }
        var exactChanges = string.Join(Environment.NewLine, tweaks.SelectMany(x => x.ApplyOperations).Select(x => $"• {x.Id} [{x.Kind}] {(x.RequiresAdmin ? "Administrator" : "Current user")}"));
        var warning = string.Join(Environment.NewLine, tweaks.Where(x => x.PossibleSideEffects.Count > 0).Select(x => $"{_localizer[x.TitleKey]}: {string.Join("; ", x.PossibleSideEffects)}"));
        if (MessageBox.Show($"Exact changes:{Environment.NewLine}{exactChanges}{Environment.NewLine}{Environment.NewLine}Possible side effects:{Environment.NewLine}{warning}{Environment.NewLine}{Environment.NewLine}A before-state journal will be written before execution. Continue?", "WinForge — review", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var plan = new CatalogEngine().CreatePlan(_loadedCatalog.Catalog, tweaks.Select(x => x.Id), _snapshot, _loadedCatalog.Hash);
        SetBusy(true);
        try
        {
            var response = await _worker.ExecuteAsync(plan);
            Status = response.Success ? $"Applied and verified. Rollback session: {response.Session?.SessionId}" : $"Not applied: {response.Message}";
        }
        finally { SetBusy(false); }
    }

    private async Task RollbackLatestAsync()
    {
        if (_latestSessionId is null) return;
        var directory = Path.Combine(_journal.RootPath, _latestSessionId.Value.ToString("D"));
        var planPath = Path.Combine(directory, "operations.json");
        var session = await _journal.ReadSessionAsync(_latestSessionId.Value);
        if (session is null || !File.Exists(planPath)) { Status = "The selected rollback journal is incomplete."; return; }
        await using var stream = File.OpenRead(planPath);
        var plan = await System.Text.Json.JsonSerializer.DeserializeAsync<ExecutionPlan>(stream, JsonDefaults.Options);
        if (plan is null) { Status = "The execution plan could not be read."; return; }
        if (MessageBox.Show($"Restore exact original state for session {_latestSessionId}? Current external changes will be verified before overwrite where supported.", "WinForge rollback", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        SetBusy(true);
        try { var response = await _worker.ExecuteAsync(plan, session); Status = response.Success ? "Rollback completed and verified." : $"Rollback failed: {response.Message}"; }
        finally { SetBusy(false); }
    }

    private async Task CreateSupportBundleAsync()
    {
        if (_snapshot is null) return; SetBusy(true);
        try { Status = $"Support bundle created: {await _support.CreateAsync(_snapshot)}"; }
        catch (Exception ex) { Status = $"Support bundle failed: {ex.Message}"; }
        finally { SetBusy(false); }
    }

    private void LoadTweaks(string category)
    {
        if (_loadedCatalog is null) return;
        AddRows(_loadedCatalog.Catalog.Tweaks.Where(x => x.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).Select(x => new TweakDisplay(x.Id, _localizer[x.TitleKey], x.Category, x.RiskLevel.ToString(), x.Recommendation.ToString(), x.ExpectedImpact, string.Join("; ", x.PossibleSideEffects), x.RequiresAdmin, x.RequiresRestart, x.Reversible)));
    }

    private void LoadRepairTools() => AddRows(new[]
    {
        new RepairDisplay("DISM CheckHealth", "dism.exe /Online /Cleanup-Image /CheckHealth", "Read-only health check", "No"), new RepairDisplay("DISM ScanHealth", "dism.exe /Online /Cleanup-Image /ScanHealth", "Read-only component scan", "No"),
        new RepairDisplay("DISM RestoreHealth", "dism.exe /Online /Cleanup-Image /RestoreHealth", "Repairs component store", "Possible"), new RepairDisplay("SFC ScanNow", "sfc.exe /scannow", "Verifies and repairs protected files", "Possible"),
        new RepairDisplay("CHKDSK scan", "chkdsk.exe /scan", "Online read-only scan", "No"), new RepairDisplay("Flush DNS", "ipconfig.exe /flushdns", "Clears DNS resolver cache", "No"), new RepairDisplay("Winsock reset", "netsh.exe winsock reset", "Network repair", "Yes")
    });

    private void LoadJournals()
    {
        _latestSessionId = null;
        if (!Directory.Exists(_journal.RootPath)) return;
        var directories = Directory.EnumerateDirectories(_journal.RootPath).OrderByDescending(Directory.GetLastWriteTime).ToArray();
        _latestSessionId = directories.Select(Path.GetFileName).Select(x => Guid.TryParse(x, out var id) ? id : (Guid?)null).FirstOrDefault(x => x is not null);
        AddRows(directories.Select(x => new JournalDisplay(Path.GetFileName(x), Directory.GetLastWriteTime(x), File.Exists(Path.Combine(x, "session.json")) ? "Journal available" : "Incomplete")));
        Raise(nameof(SecondaryVisibility));
    }
    private void LoadLogs() { if (Directory.Exists(_journal.RootPath)) AddRows(Directory.EnumerateFiles(_journal.RootPath, "execution.log", SearchOption.AllDirectories).Select(x => new { File = x.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%", StringComparison.OrdinalIgnoreCase), Size = new FileInfo(x).Length, Modified = File.GetLastWriteTime(x) })); }
    private void AddRows<T>(IEnumerable<T> rows) { foreach (var row in rows) if (row is not null) _rows.Add(row); }
    private bool FilterRow(object item) => string.IsNullOrWhiteSpace(SearchText) || item.ToString()?.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) == true;
    private void SetBusy(bool value) { _busy = value; Raise(nameof(BusyVisibility)); ((AsyncCommand)PrimaryActionCommand).RaiseCanExecuteChanged(); ((AsyncCommand)SecondaryActionCommand).RaiseCanExecuteChanged(); ((AsyncCommand)SupportBundleCommand).RaiseCanExecuteChanged(); }
    private void RaisePageProperties() { Raise(nameof(PageTitle)); Raise(nameof(PageDescription)); Raise(nameof(DashboardVisibility)); Raise(nameof(AboutVisibility)); Raise(nameof(TableVisibility)); Raise(nameof(PrimaryActionLabel)); Raise(nameof(SecondaryActionLabel)); Raise(nameof(SecondaryVisibility)); }
    private void ToggleTheme() { _dark = !_dark; ApplicationThemeManager.Apply(_dark ? ApplicationTheme.Dark : ApplicationTheme.Light); }
    private void ToggleLanguage() { _localizer.SetCulture(_localizer.Culture.Name == "ar-SA" ? "en-US" : "ar-SA"); BuildNavigation(); Raise(nameof(FlowDirection)); RaisePageProperties(); Status = _localizer["Ready"]; }

    private void BuildNavigation()
    {
        var selectedId = SelectedNavigation?.Id ?? "dashboard"; Navigation.Clear();
        Navigation.Add(new("dashboard", _localizer["Dashboard"], "▦", "System facts and conservative diagnostics.")); Navigation.Add(new("cleaner", _localizer["Cleaner"], "◫", "Preview first. Never follows junctions or deletes personal folders."));
        Navigation.Add(new("startup", _localizer["Startup"], "↗", "Registry and Startup folder entries with publisher and source.")); Navigation.Add(new("apps", _localizer["Apps"], "▤", "Installed Appx packages with protected-item classification."));
        Navigation.Add(new("services", _localizer["Services"], "⚙", "Service status and startup configuration. Protected services are blocked.")); Navigation.Add(new("tasks", _localizer["Tasks"], "◷", "Scheduled task identity, state, triggers and actions."));
        Navigation.Add(new("features", "Optional Features", "◇", "Actual DISM optional-feature state.")); Navigation.Add(new("privacy", _localizer["Privacy"], "◉", "Documented, reversible privacy settings."));
        Navigation.Add(new("performance", _localizer["Performance"], "⌁", "Measured diagnostics; no magic timer or registry claims.")); Navigation.Add(new("gaming", _localizer["Gaming"], "🎮", "Game Mode and capture settings without anti-cheat or security changes."));
        Navigation.Add(new("power", _localizer["Power"], "ϟ", "Power scheme inspector with duplicate-plan prevention.")); Navigation.Add(new("network", _localizer["Network"], "⌘", "Adapters, link speed, DNS, MTU and DHCP status."));
        Navigation.Add(new("repair", _localizer["Repair"], "✚", "Allowlisted Windows repair tools with exact commands.")); Navigation.Add(new("rollback", _localizer["Rollback"], "↶", "Exact before-state journals for every execution session."));
        Navigation.Add(new("logs", _localizer["Logs"], "≡", "Structured local logs with user paths sanitized.")); Navigation.Add(new("settings", _localizer["Settings"], "⚙", "Theme and language are stored locally. Update checking is off.")); Navigation.Add(new("about", _localizer["About"], "ⓘ", "MIT-licensed, local-first and telemetry-free."));
        _selectedNavigation = Navigation.First(x => x.Id == selectedId); Raise(nameof(SelectedNavigation));
    }

    private static string FormatBytes(long bytes) { string[] units = ["B", "KB", "MB", "GB", "TB"]; double value = bytes; var index = 0; while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; } return $"{value:0.##} {units[index]}"; }
}
