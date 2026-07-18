using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.Infrastructure;

public sealed class WindowsSystemScanner(SafeProcessRunner runner, IStartupReader startupReader, IScheduledTaskReader taskReader) : ISystemScanner
{
    public async Task<SystemSnapshot> ScanAsync(CancellationToken cancellationToken = default)
    {
        var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var edition = cv?.GetValue("EditionID")?.ToString() ?? "Unknown";
        var product = cv?.GetValue("ProductName")?.ToString() ?? RuntimeInformation.OSDescription;
        var build = int.TryParse(cv?.GetValue("CurrentBuildNumber")?.ToString(), out var parsed) ? parsed : Environment.OSVersion.Version.Build;
        var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0")?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "Unknown";
        var gpu = await ReadGpuAsync(cancellationToken).ConfigureAwait(false);
        var power = await runner.RunAsync("powercfg.exe", ["/getactivescheme"], TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        var startupTask = startupReader.GetEntriesAsync(cancellationToken);
        var scheduledTask = taskReader.GetEntriesAsync(cancellationToken);
        var drives = DriveInfo.GetDrives().Where(x => x.IsReady).Select(x => new DriveSnapshot(x.Name, x.DriveFormat, x.TotalSize, x.AvailableFreeSpace, GetMediaType(x.RootDirectory.FullName))).ToArray();
        var processCount = Process.GetProcesses().CountProcessesAndDispose();
        return new(product, edition, build, SystemInformationProbe.HasBattery(), cpu, gpu,
            SystemInformationProbe.GetPhysicalMemoryBytes(), drives, power.StandardOutput.Trim(),
            TimeSpan.FromMilliseconds(Environment.TickCount64), (await startupTask.ConfigureAwait(false)).Count,
            processCount, (await scheduledTask.ConfigureAwait(false)).Count, DateTimeOffset.Now);
    }

    private async Task<string> ReadGpuAsync(CancellationToken cancellationToken)
    {
        var result = await runner.RunPowerShellQueryAsync(ReadOnlyPowerShellPolicy.SystemInfo, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput)) return "Unknown";
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(result.StandardOutput);
            return json.RootElement.TryGetProperty("Name", out var name) ? name.GetString() ?? "Unknown" : "Unknown";
        }
        catch (System.Text.Json.JsonException) { return "Unknown"; }
    }

    private static string GetMediaType(string root) => GetDriveType(root) switch
    {
        2 => "Removable",
        3 => "Local disk",
        4 => "Network",
        5 => "Optical",
        _ => "Unknown"
    };

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetDriveType(string lpRootPathName);
}

internal static class ProcessExtensions
{
    public static int CountProcessesAndDispose(this Process[] processes)
    {
        try { return processes.Length; }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}

internal static class SystemInformationProbe
{
    public static long GetPhysicalMemoryBytes()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? checked((long)status.TotalPhysical) : 0;
    }

    public static bool HasBattery()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
            if (key?.GetValue("EnergyEstimationEnabled") is not null) return true;
            return Directory.Exists(@"C:\Windows\System32\DriverStore\FileRepository") &&
                   Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\ACPI")?.GetSubKeyNames().Any(x => x.StartsWith("PNP0C0A", StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch (UnauthorizedAccessException) { return false; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length; public uint MemoryLoad; public ulong TotalPhysical; public ulong AvailablePhysical;
        public ulong TotalPageFile; public ulong AvailablePageFile; public ulong TotalVirtual; public ulong AvailableVirtual; public ulong AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
