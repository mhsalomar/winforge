using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.Infrastructure;

public sealed class StartupReader : IStartupReader
{
    public Task<IReadOnlyList<StartupEntry>> GetEntriesAsync(CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<StartupEntry>>(() =>
    {
        var entries = new List<StartupEntry>();
        ReadRegistry(entries, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU Run");
        ReadRegistry(entries, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "HKCU RunOnce");
        ReadRegistry(entries, Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKLM Run");
        ReadRegistry(entries, Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "HKLM RunOnce");
        ReadFolder(entries, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Startup folder");
        ReadFolder(entries, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Common startup folder");
        return entries;
    }, cancellationToken);

    private static void ReadRegistry(List<StartupEntry> entries, RegistryKey hive, string path, string source)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            if (key is null) return;
            foreach (var name in key.GetValueNames())
            {
                var command = key.GetValue(name)?.ToString() ?? string.Empty;
                entries.Add(new(name, command, source, true, PublisherProbe.FromCommand(command), PathProbe.FromCommand(command)));
            }
        }
        catch (UnauthorizedAccessException) { }
    }

    private static void ReadFolder(List<StartupEntry> entries, string folder, string source)
    {
        if (!Directory.Exists(folder)) return;
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder))
                entries.Add(new(Path.GetFileNameWithoutExtension(path), path, source, true, PublisherProbe.FromFile(path), path));
        }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class ServiceReader(SafeProcessRunner runner) : IServiceReader
{
    public async Task<IReadOnlyList<ServiceEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await JsonQuery.ReadArrayAsync<ServiceRow>(runner, ReadOnlyPowerShellPolicy.Services, cancellationToken).ConfigureAwait(false);
        return rows.Select(x => new ServiceEntry(x.Name ?? string.Empty, x.DisplayName ?? x.Name ?? string.Empty, x.State ?? "Unknown", x.StartMode ?? "Unknown",
            x.ProcessId > 0 ? x.ProcessId : null, x.PathName ?? string.Empty, Classify(x.Name, x.PathName))).ToArray();
    }

    private static string Classify(string? name, string? path)
    {
        if (path?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true || path?.Contains("Windows", StringComparison.OrdinalIgnoreCase) == true) return "Microsoft Features";
        if (name is not null && ProtectedServices.Names.Contains(name)) return "Core System";
        return "Third-party / Unknown";
    }

    private sealed record ServiceRow(string? Name, string? DisplayName, string? State, string? StartMode, int ProcessId, string? PathName);
}

public sealed class ScheduledTaskReader(SafeProcessRunner runner) : IScheduledTaskReader
{
    public async Task<IReadOnlyList<ScheduledTaskEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await JsonQuery.ReadArrayAsync<TaskRow>(runner, ReadOnlyPowerShellPolicy.Tasks, cancellationToken).ConfigureAwait(false);
        return rows.Select(x => new ScheduledTaskEntry(x.TaskPath ?? "\\", x.TaskName ?? string.Empty, x.State ?? "Unknown", x.Triggers ?? [], x.Actions ?? [])).ToArray();
    }

    private sealed record TaskRow(string? TaskPath, string? TaskName, string? State, IReadOnlyList<string>? Triggers, IReadOnlyList<string>? Actions);
}

public sealed class AppxReader(SafeProcessRunner runner) : IAppxReader
{
    public async Task<IReadOnlyList<AppxEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await JsonQuery.ReadArrayAsync<AppxRow>(runner, ReadOnlyPowerShellPolicy.Appx, cancellationToken).ConfigureAwait(false);
        return rows.Select(x => new AppxEntry(x.Name ?? string.Empty, x.PackageFullName ?? string.Empty, x.Publisher ?? string.Empty, x.Version ?? string.Empty, false, AppxClassification.Classify(x.Name))).ToArray();
    }
    private sealed record AppxRow(string? Name, string? PackageFullName, string? Publisher, string? Version);
}

public sealed class OptionalFeatureReader(SafeProcessRunner runner) : IOptionalFeatureReader
{
    public async Task<IReadOnlyList<OptionalFeatureEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await JsonQuery.ReadArrayAsync<FeatureRow>(runner, ReadOnlyPowerShellPolicy.Features, cancellationToken).ConfigureAwait(false);
        return rows.Select(x => new OptionalFeatureEntry(x.FeatureName ?? string.Empty, x.State ?? "Unknown", FeatureClassification.Classify(x.FeatureName))).ToArray();
    }
    private sealed record FeatureRow(string? FeatureName, string? State);
}

public sealed class NetworkReader : INetworkReader
{
    public Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync(CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<NetworkAdapterInfo>>(() =>
        NetworkInterface.GetAllNetworkInterfaces().Where(x => x.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel).Select(x =>
        {
            var properties = x.GetIPProperties();
            var dns = string.Join(", ", properties.DnsAddresses.Select(a => a.ToString()));
            var mtu = properties.GetIPv4Properties()?.Mtu;
            return new NetworkAdapterInfo(x.Name, x.OperationalStatus.ToString(), x.Speed, dns, mtu, properties.GetIPv4Properties()?.IsDhcpEnabled == true);
        }).ToArray(), cancellationToken);
}

internal static class JsonQuery
{
    public static async Task<IReadOnlyList<T>> ReadArrayAsync<T>(SafeProcessRunner runner, string script, CancellationToken cancellationToken)
    {
        var result = await runner.RunPowerShellQueryAsync(script, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput)) return [];
        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
                return document.RootElement.Deserialize<T[]>(JsonDefaults.Options) ?? [];
            var single = document.RootElement.Deserialize<T>(JsonDefaults.Options);
            return single is null ? [] : [single];
        }
        catch (JsonException) { return []; }
    }
}

internal static class PathProbe
{
    public static string? FromCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed[1..end] : null;
        }
        var exe = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe >= 0 ? trimmed[..(exe + 4)] : trimmed.Split(' ', 2)[0];
    }
}

internal static class PublisherProbe
{
    public static string FromCommand(string command) => FromFile(PathProbe.FromCommand(command));
    public static string FromFile(string? path)
    {
        try { return path is not null && File.Exists(path) ? System.Diagnostics.FileVersionInfo.GetVersionInfo(path).CompanyName ?? "Unknown" : "Unknown"; }
        catch (System.Security.SecurityException) { return "Unknown"; }
    }
}

internal static class ProtectedServices
{
    public static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "wuauserv", "BITS", "CryptSvc", "WinDefend", "mpssvc", "Dhcp", "Dnscache", "NlaSvc", "EventLog", "RpcSs", "Schedule", "PlugPlay",
        "msiserver", "Audiosrv", "AudioEndpointBuilder", "ProfSvc", "AppXSvc", "InstallService", "GamingServices", "GamingServicesNet", "BFE", "LSM"
    };
}

internal static class AppxClassification
{
    private static readonly string[] Protected = ["WindowsStore", "DesktopAppInstaller", "SecHealthUI", "ShellExperienceHost", "StartMenuExperienceHost", "WebExperience", "WebView"];
    private static readonly string[] Gaming = ["Xbox", "GamingApp", "GamingServices"];
    public static string Classify(string? name) => name is null ? "Unknown" : Protected.Any(name.Contains) ? "System protected" : Gaming.Any(name.Contains) ? "Gaming/Xbox" : name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ? "Microsoft optional" : "User apps";
}

internal static class FeatureClassification
{
    public static string Classify(string? name) => name switch
    {
        null => "Unknown",
        var x when x.Contains("SMB1", StringComparison.OrdinalIgnoreCase) => "Legacy / Security risk",
        var x when x.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) || x.Contains("VirtualMachinePlatform", StringComparison.OrdinalIgnoreCase) || x.Contains("Subsystem-Linux", StringComparison.OrdinalIgnoreCase) => "Virtualization",
        var x when x.Contains("IIS", StringComparison.OrdinalIgnoreCase) => "Web server",
        _ => "Optional"
    };
}
