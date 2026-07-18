using System.Diagnostics;
using System.Text;

namespace WinForge.Infrastructure;

public sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError, TimeSpan Duration, bool TimedOut);

public sealed class SafeProcessRunner
{
    private static readonly HashSet<string> AllowedExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "powercfg.exe", "schtasks.exe", "sc.exe", "dism.exe", "sfc.exe", "chkdsk.exe",
        "wsreset.exe", "wevtutil.exe", "ipconfig.exe", "netsh.exe"
    };

    public async Task<ProcessRunResult> RunAsync(string executable, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var fileName = Path.GetFileName(executable);
        if (!AllowedExecutables.Contains(fileName)) throw new InvalidOperationException($"Executable is not allowlisted: {fileName}");
        return await RunCoreAsync(executable, arguments, timeout, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ProcessRunResult> RunCoreAsync(string executable, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var args = arguments.ToArray();
        if (args.Any(x => x.Contains('\0') || x.Contains('\r') || x.Contains('\n'))) throw new ArgumentException("Arguments contain invalid control characters.", nameof(arguments));
        if (args.Sum(x => x.Length) > 32_768) throw new ArgumentException("Arguments exceed the safe length limit.", nameof(arguments));

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
        foreach (var argument in args) process.StartInfo.ArgumentList.Add(argument);
        var started = Stopwatch.StartNew();
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            return new(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false), started.Elapsed, false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            return new(-1, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false), started.Elapsed, true);
        }
    }

    public Task<ProcessRunResult> RunPowerShellQueryAsync(string script, CancellationToken cancellationToken = default)
    {
        if (!ReadOnlyPowerShellPolicy.IsAllowed(script)) throw new InvalidOperationException("PowerShell query is not an approved built-in query.");
        return RunCoreAsync("powershell.exe", ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script], TimeSpan.FromSeconds(30), cancellationToken);
    }
}

internal static class ReadOnlyPowerShellPolicy
{
    internal const string SystemInfo = "$ProgressPreference='SilentlyContinue'; Get-CimInstance Win32_VideoController | Select-Object -First 1 Name | ConvertTo-Json -Compress";
    internal const string Services = "$ProgressPreference='SilentlyContinue'; Get-CimInstance Win32_Service | Select-Object Name,DisplayName,State,StartMode,ProcessId,PathName | ConvertTo-Json -Compress";
    internal const string Tasks = "$ProgressPreference='SilentlyContinue'; Get-ScheduledTask | Select-Object TaskPath,TaskName,State,@{n='Triggers';e={@($_.Triggers | ForEach-Object {$_.CimClass.CimClassName})}},@{n='Actions';e={@($_.Actions | ForEach-Object {$_.Execute})}} | ConvertTo-Json -Depth 4 -Compress";
    internal const string Appx = "$ProgressPreference='SilentlyContinue'; Get-AppxPackage | Select-Object Name,PackageFullName,Publisher,Version | ConvertTo-Json -Compress";
    internal const string Features = "$ProgressPreference='SilentlyContinue'; Get-WindowsOptionalFeature -Online | Select-Object FeatureName,State | ConvertTo-Json -Compress";
    internal const string Network = "$ProgressPreference='SilentlyContinue'; Get-CimInstance Win32_NetworkAdapterConfiguration -Filter 'IPEnabled=True' | Select-Object Description,DHCPEnabled,@{n='Dns';e={($_.DNSServerSearchOrder -join ', ')}} | ConvertTo-Json -Compress";

    private static readonly HashSet<string> Queries = new(StringComparer.Ordinal)
    {
        SystemInfo, Services, Tasks, Appx, Features, Network
    };

    public static bool IsAllowed(string script) => Queries.Contains(script);
}
