using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.Infrastructure;

public sealed class WindowsOperationExecutor(SafeProcessRunner runner) : IOperationExecutor
{
    public async Task<OperationBeforeState> CaptureBeforeAsync(TypedOperation operation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return operation switch
        {
            RegistryOperation registry => CaptureRegistry(registry),
            ServiceOperation service => await CaptureServiceAsync(service, cancellationToken).ConfigureAwait(false),
            ScheduledTaskOperation task => await CaptureTaskAsync(task, cancellationToken).ConfigureAwait(false),
            PowerOperation power when power.Kind == OperationKind.PowerSchemeActivate => await CaptureActivePowerSchemeAsync(power, cancellationToken).ConfigureAwait(false),
            _ => new OperationBeforeState(operation.Id, false, null, null, new Dictionary<string, string> { ["capture"] = "State capture is not supported for this operation type." })
        };
    }

    public async Task<OperationResult> ExecuteAsync(TypedOperation operation, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return operation switch
            {
                RegistryOperation registry => ExecuteRegistry(registry, stopwatch),
                ServiceOperation service => await ExecuteServiceAsync(service, stopwatch, cancellationToken).ConfigureAwait(false),
                ScheduledTaskOperation task => await ExecuteTaskAsync(task, stopwatch, cancellationToken).ConfigureAwait(false),
                OptionalFeatureOperation feature => await ExecuteFeatureAsync(feature, stopwatch, cancellationToken).ConfigureAwait(false),
                PowerOperation power => await ExecutePowerAsync(power, stopwatch, cancellationToken).ConfigureAwait(false),
                FileOperation file => await ExecuteFileAsync(file, stopwatch, cancellationToken).ConfigureAwait(false),
                CommandOperation command => await ExecuteCommandAsync(command, stopwatch, cancellationToken).ConfigureAwait(false),
                _ => new(operation.Id, false, false, null, "Operation is not implemented by this worker.", stopwatch.Elapsed)
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            return new(operation.Id, false, false, null, ex.Message, stopwatch.Elapsed);
        }
    }

    public Task<OperationResult> RollbackAsync(TypedOperation operation, OperationBeforeState beforeState, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        if (operation is ServiceOperation service) return RollbackServiceAsync(service, beforeState, stopwatch, cancellationToken);
        if (operation is ScheduledTaskOperation task) return RollbackTaskAsync(task, beforeState, stopwatch, cancellationToken);
        if (operation is PowerOperation power && power.Kind == OperationKind.PowerSchemeActivate) return RollbackPowerAsync(power, beforeState, stopwatch, cancellationToken);
        if (operation is not RegistryOperation registry) return Task.FromResult(new OperationResult(operation.Id, false, false, null, "Automatic rollback is unavailable for this operation type.", stopwatch.Elapsed));
        try
        {
            ValidateRegistry(registry);
            using var baseKey = OpenHive(registry.Hive);
            if (registry.ValueName is null) return Task.FromResult(new OperationResult(operation.Id, false, false, null, "Registry value name is required for rollback.", stopwatch.Elapsed));
            if (!beforeState.Existed)
            {
                using var key = baseKey.OpenSubKey(registry.KeyPath, true);
                key?.DeleteValue(registry.ValueName, false);
                var verified = baseKey.OpenSubKey(registry.KeyPath)?.GetValue(registry.ValueName) is null;
                return Task.FromResult(new OperationResult(operation.Id, verified, verified, 0, verified ? "Original absence restored." : "Failed to delete value.", stopwatch.Elapsed));
            }
            using (var key = baseKey.CreateSubKey(registry.KeyPath, true)) key.SetValue(registry.ValueName, beforeState.Value!, ParseKind(beforeState.ValueType));
            var current = baseKey.OpenSubKey(registry.KeyPath)?.GetValue(registry.ValueName);
            var matches = EqualsNormalized(current, beforeState.Value);
            return Task.FromResult(new OperationResult(operation.Id, matches, matches, 0, matches ? "Original registry value restored." : "Rollback verification failed.", stopwatch.Elapsed));
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException or IOException)
        {
            return Task.FromResult(new OperationResult(operation.Id, false, false, null, ex.Message, stopwatch.Elapsed));
        }
    }

    private async Task<OperationBeforeState> CaptureServiceAsync(ServiceOperation operation, CancellationToken cancellationToken)
    {
        ValidateIdentifier(operation.ServiceName, "service");
        var config = await runner.RunAsync("sc.exe", ["qc", operation.ServiceName], TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        if (config.ExitCode != 0) return new(operation.Id, false, null, null);
        var startMatch = Regex.Match(config.StandardOutput, @"START_TYPE\s*:\s*\d+\s+(\S+)", RegexOptions.IgnoreCase);
        var state = await runner.RunAsync("sc.exe", ["query", operation.ServiceName], TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        var stateMatch = Regex.Match(state.StandardOutput, @"STATE\s*:\s*\d+\s+(\S+)", RegexOptions.IgnoreCase);
        return new(operation.Id, true, null, null, new Dictionary<string, string> { ["startupType"] = NormalizeScStartup(startMatch.Groups[1].Value), ["running"] = (stateMatch.Groups[1].Value.Equals("RUNNING", StringComparison.OrdinalIgnoreCase)).ToString() });
    }

    private async Task<OperationBeforeState> CaptureTaskAsync(ScheduledTaskOperation operation, CancellationToken cancellationToken)
    {
        ValidateTaskPath(operation.TaskPath, operation.TaskName);
        var identity = operation.TaskPath.TrimEnd('\\') + "\\" + operation.TaskName;
        var result = await runner.RunAsync("schtasks.exe", ["/Query", "/TN", identity, "/XML"], TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) return new(operation.Id, false, null, null);
        try
        {
            var document = XDocument.Parse(result.StandardOutput);
            var enabled = document.Descendants().FirstOrDefault(x => x.Name.LocalName == "Enabled")?.Value ?? "true";
            return new(operation.Id, true, null, null, new Dictionary<string, string> { ["enabled"] = enabled });
        }
        catch (System.Xml.XmlException) { return new(operation.Id, true, null, null, new Dictionary<string, string> { ["capture"] = "Task XML could not be parsed." }); }
    }

    private async Task<OperationBeforeState> CaptureActivePowerSchemeAsync(PowerOperation operation, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync("powercfg.exe", ["/getactivescheme"], TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        var match = Regex.Match(result.StandardOutput, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        return new(operation.Id, match.Success, null, match.Success ? match.Value : null);
    }

    private async Task<OperationResult> RollbackServiceAsync(ServiceOperation operation, OperationBeforeState beforeState, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        if (!beforeState.Existed || beforeState.Metadata is null) return new(operation.Id, false, false, null, "Original service state is unavailable.", stopwatch.Elapsed);
        var startup = beforeState.Metadata.GetValueOrDefault("startupType");
        var config = await runner.RunAsync("sc.exe", ["config", operation.ServiceName, "start=", ValidateStartupType(startup)], TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        if (config.ExitCode != 0) return new(operation.Id, false, false, config.ExitCode, config.StandardError, stopwatch.Elapsed);
        var shouldRun = bool.TryParse(beforeState.Metadata.GetValueOrDefault("running"), out var running) && running;
        var state = await runner.RunAsync("sc.exe", [shouldRun ? "start" : "stop", operation.ServiceName], TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        return new(operation.Id, state.ExitCode == 0 || (!shouldRun && state.ExitCode == 1062), true, state.ExitCode, "Service startup and running state restored.", stopwatch.Elapsed);
    }

    private async Task<OperationResult> RollbackTaskAsync(ScheduledTaskOperation operation, OperationBeforeState beforeState, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        if (!beforeState.Existed || beforeState.Metadata is null) return new(operation.Id, false, false, null, "Original task state is unavailable.", stopwatch.Elapsed);
        var enabled = !string.Equals(beforeState.Metadata.GetValueOrDefault("enabled"), "false", StringComparison.OrdinalIgnoreCase);
        var identity = operation.TaskPath.TrimEnd('\\') + "\\" + operation.TaskName;
        var result = await runner.RunAsync("schtasks.exe", ["/Change", "/TN", identity, enabled ? "/ENABLE" : "/DISABLE"], TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        return new(operation.Id, result.ExitCode == 0, result.ExitCode == 0, result.ExitCode, result.StandardError + result.StandardOutput, stopwatch.Elapsed);
    }

    private async Task<OperationResult> RollbackPowerAsync(PowerOperation operation, OperationBeforeState beforeState, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        if (beforeState.Value is not string guid || !Guid.TryParse(guid, out _)) return new(operation.Id, false, false, null, "Original active scheme is unavailable.", stopwatch.Elapsed);
        var result = await runner.RunAsync("powercfg.exe", ["/setactive", guid], TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        return new(operation.Id, result.ExitCode == 0, result.ExitCode == 0, result.ExitCode, result.StandardError + result.StandardOutput, stopwatch.Elapsed);
    }

    private static Task<OperationResult> ExecuteFileAsync(FileOperation operation, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation.AllowlistedRoot is null) throw new InvalidOperationException("File operation requires an allowlisted root.");
        var root = Path.GetFullPath(operation.AllowlistedRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(operation.Path);
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("File path escapes the allowlisted root.");
        var attributes = File.Exists(path) ? File.GetAttributes(path) : Directory.Exists(path) ? new DirectoryInfo(path).Attributes : 0;
        if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Reparse points are blocked.");
        if (operation.Kind == OperationKind.FileDelete && File.Exists(path)) File.Delete(path);
        else if (operation.Kind == OperationKind.DirectoryContentsDelete && Directory.Exists(path))
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly)) if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) File.Delete(file);
        }
        var success = operation.Kind == OperationKind.FileDelete ? !File.Exists(path) : true;
        return Task.FromResult(new OperationResult(operation.Id, success, success, 0, success ? "File operation verified." : "File operation verification failed.", stopwatch.Elapsed));
    }

    private async Task<OperationResult> ExecuteCommandAsync(CommandOperation operation, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var command = operation.CommandId switch
        {
            "dism.check-health" => ("dism.exe", new[] { "/Online", "/Cleanup-Image", "/CheckHealth" }, TimeSpan.FromMinutes(2)),
            "dism.scan-health" => ("dism.exe", new[] { "/Online", "/Cleanup-Image", "/ScanHealth" }, TimeSpan.FromMinutes(20)),
            "dism.restore-health" => ("dism.exe", new[] { "/Online", "/Cleanup-Image", "/RestoreHealth" }, TimeSpan.FromMinutes(60)),
            "sfc.scan" => ("sfc.exe", new[] { "/scannow" }, TimeSpan.FromMinutes(60)),
            "chkdsk.scan" => ("chkdsk.exe", new[] { "/scan" }, TimeSpan.FromMinutes(30)),
            "dns.flush" => ("ipconfig.exe", new[] { "/flushdns" }, TimeSpan.FromSeconds(30)),
            "dhcp.renew" => ("ipconfig.exe", new[] { "/renew" }, TimeSpan.FromMinutes(2)),
            "winsock.reset" => ("netsh.exe", new[] { "winsock", "reset" }, TimeSpan.FromMinutes(2)),
            "tcpip.reset" => ("netsh.exe", new[] { "int", "ip", "reset" }, TimeSpan.FromMinutes(2)),
            "store.reset" => ("wsreset.exe", Array.Empty<string>(), TimeSpan.FromMinutes(5)),
            _ => throw new InvalidOperationException("Command id is not allowlisted.")
        };
        if (operation.Arguments.Count > 0) throw new InvalidOperationException("This command does not accept catalog arguments.");
        var result = await runner.RunAsync(command.Item1, command.Item2, command.Item3, cancellationToken).ConfigureAwait(false);
        return new(operation.Id, result.ExitCode == 0, result.ExitCode == 0, result.ExitCode, result.StandardError + result.StandardOutput, stopwatch.Elapsed);
    }

    private static OperationBeforeState CaptureRegistry(RegistryOperation operation)
    {
        ValidateRegistry(operation);
        using var baseKey = OpenHive(operation.Hive);
        using var key = baseKey.OpenSubKey(operation.KeyPath);
        if (key is null || operation.ValueName is null || !key.GetValueNames().Contains(operation.ValueName, StringComparer.OrdinalIgnoreCase))
            return new(operation.Id, false, null, null);
        return new(operation.Id, true, key.GetValueKind(operation.ValueName).ToString(), key.GetValue(operation.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
    }

    private static OperationResult ExecuteRegistry(RegistryOperation operation, Stopwatch stopwatch)
    {
        ValidateRegistry(operation);
        using var baseKey = OpenHive(operation.Hive);
        if (operation.Kind == OperationKind.RegistryCreateKey)
        {
            using var created = baseKey.CreateSubKey(operation.KeyPath, true);
            var verified = created is not null;
            return new(operation.Id, verified, verified, 0, verified ? "Registry key created." : "Registry key creation failed.", stopwatch.Elapsed);
        }
        if (operation.Kind == OperationKind.RegistryDeleteKey)
        {
            if (!operation.KeyPath.StartsWith(@"Software\WinForge", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Registry key deletion is restricted to WinForge-owned keys.");
            baseKey.DeleteSubKeyTree(operation.KeyPath, false);
            var verified = baseKey.OpenSubKey(operation.KeyPath) is null;
            return new(operation.Id, verified, verified, 0, "Registry key deletion verified.", stopwatch.Elapsed);
        }
        if (operation.ValueName is null) throw new ArgumentException("Registry value name is required.");
        using var key = baseKey.CreateSubKey(operation.KeyPath, true);
        if (operation.Kind == OperationKind.RegistryDeleteValue)
        {
            key.DeleteValue(operation.ValueName, false);
            var verified = !key.GetValueNames().Contains(operation.ValueName, StringComparer.OrdinalIgnoreCase);
            return new(operation.Id, verified, verified, 0, "Registry value absence verified.", stopwatch.Elapsed);
        }
        key.SetValue(operation.ValueName, NormalizeJsonValue(operation.Value), ParseKind(operation.ValueKind));
        var actual = key.GetValue(operation.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        var success = EqualsNormalized(actual, NormalizeJsonValue(operation.Value));
        return new(operation.Id, success, success, 0, success ? "Registry value applied and verified." : "Registry verification failed.", stopwatch.Elapsed);
    }

    private async Task<OperationResult> ExecuteServiceAsync(ServiceOperation operation, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        ValidateIdentifier(operation.ServiceName, "service");
        if (ProtectedServices.Names.Contains(operation.ServiceName) && operation.Kind is OperationKind.ServiceStop or OperationKind.ServiceSetStartupType && string.Equals(operation.StartupType, "disabled", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Protected service changes are blocked.");
        var arguments = operation.Kind switch
        {
            OperationKind.ServiceStart => new[] { "start", operation.ServiceName },
            OperationKind.ServiceStop => new[] { "stop", operation.ServiceName },
            OperationKind.ServiceSetStartupType => new[] { "config", operation.ServiceName, "start=", ValidateStartupType(operation.StartupType) },
            _ => throw new InvalidOperationException("Invalid service operation.")
        };
        var result = await runner.RunAsync("sc.exe", arguments, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        return new(operation.Id, result.ExitCode == 0, result.ExitCode == 0, result.ExitCode, result.StandardError + result.StandardOutput, stopwatch.Elapsed);
    }

    private async Task<OperationResult> ExecuteTaskAsync(ScheduledTaskOperation operation, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        ValidateTaskPath(operation.TaskPath, operation.TaskName);
        var change = operation.Kind == OperationKind.ScheduledTaskEnable ? "/ENABLE" : "/DISABLE";
        var result = await runner.RunAsync("schtasks.exe", ["/Change", "/TN", operation.TaskPath.TrimEnd('\\') + "\\" + operation.TaskName, change], TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        return new(operation.Id, result.ExitCode == 0, result.ExitCode == 0, result.ExitCode, result.StandardError + result.StandardOutput, stopwatch.Elapsed);
    }

    private async Task<OperationResult> ExecuteFeatureAsync(OptionalFeatureOperation operation, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        ValidateIdentifier(operation.FeatureName, "feature");
        var action = operation.Kind == OperationKind.OptionalFeatureEnable ? "/Enable-Feature" : "/Disable-Feature";
        var result = await runner.RunAsync("dism.exe", ["/Online", action, $"/FeatureName:{operation.FeatureName}", "/NoRestart"], TimeSpan.FromMinutes(10), cancellationToken).ConfigureAwait(false);
        return new(operation.Id, result.ExitCode == 0, result.ExitCode == 0, result.ExitCode, result.StandardError + result.StandardOutput, stopwatch.Elapsed);
    }

    private async Task<OperationResult> ExecutePowerAsync(PowerOperation operation, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var arguments = operation.Kind switch
        {
            OperationKind.PowerSchemeActivate => new[] { "/setactive", ValidateGuid(operation.SchemeGuid) },
            OperationKind.PowerSchemeDuplicate => new[] { "/duplicatescheme", ValidateGuid(operation.SchemeGuid) },
            OperationKind.PowerSchemeDelete => new[] { "/delete", ValidateGuid(operation.SchemeGuid) },
            OperationKind.PowerSettingSetAc => new[] { "/setacvalueindex", ValidateGuid(operation.SchemeGuid), ValidateGuid(operation.SubgroupGuid), ValidateGuid(operation.SettingGuid), ValidateValue(operation.Value) },
            OperationKind.PowerSettingSetDc => new[] { "/setdcvalueindex", ValidateGuid(operation.SchemeGuid), ValidateGuid(operation.SubgroupGuid), ValidateGuid(operation.SettingGuid), ValidateValue(operation.Value) },
            _ => throw new InvalidOperationException("Unsupported power operation.")
        };
        var result = await runner.RunAsync("powercfg.exe", arguments, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        return new(operation.Id, result.ExitCode == 0, result.ExitCode == 0, result.ExitCode, result.StandardError + result.StandardOutput, stopwatch.Elapsed);
    }

    private static void ValidateRegistry(RegistryOperation operation)
    {
        if (operation.Hive is not ("HKCU" or "HKLM")) throw new ArgumentException("Only HKCU and HKLM are supported.");
        if (string.IsNullOrWhiteSpace(operation.KeyPath) || operation.KeyPath.Contains("..", StringComparison.Ordinal) || operation.KeyPath.StartsWith('\\')) throw new ArgumentException("Invalid registry path.");
        if (operation.ValueName?.Contains('\\') == true) throw new ArgumentException("Invalid registry value name.");
    }

    private static RegistryKey OpenHive(string hive) => hive == "HKCU" ? RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default) : RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
    private static RegistryValueKind ParseKind(string? kind) => Enum.TryParse<RegistryValueKind>(kind, true, out var parsed) ? parsed : RegistryValueKind.DWord;
    private static object NormalizeJsonValue(object? value) => value is System.Text.Json.JsonElement element ? element.ValueKind switch { System.Text.Json.JsonValueKind.Number when element.TryGetInt32(out var n) => n, System.Text.Json.JsonValueKind.String => element.GetString() ?? string.Empty, _ => element.ToString() } : value ?? 0;
    private static bool EqualsNormalized(object? left, object? right) => string.Equals(Convert.ToString(left, System.Globalization.CultureInfo.InvariantCulture), Convert.ToString(right, System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    private static void ValidateIdentifier(string value, string label) { if (string.IsNullOrWhiteSpace(value) || value.Any(x => !(char.IsLetterOrDigit(x) || x is '_' or '-' or '.'))) throw new ArgumentException($"Invalid {label} identifier."); }
    private static string ValidateStartupType(string? value) => value?.ToLowerInvariant() switch { "auto" => "auto", "demand" => "demand", "disabled" => "disabled", "delayed-auto" => "delayed-auto", _ => throw new ArgumentException("Invalid service startup type.") };
    private static string NormalizeScStartup(string value) => value.ToUpperInvariant() switch { "AUTO_START" => "auto", "DEMAND_START" => "demand", "DISABLED" => "disabled", _ => "demand" };
    private static void ValidateTaskPath(string path, string name) { if (!path.StartsWith('\\') || path.Contains("..", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(name) || name.Contains('\\')) throw new ArgumentException("Invalid scheduled task identity."); }
    private static string ValidateGuid(string? value) => Guid.TryParse(value, out var guid) ? guid.ToString("D") : throw new ArgumentException("Invalid GUID.");
    private static string ValidateValue(int? value) => value is >= 0 and <= 100_000 ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : throw new ArgumentOutOfRangeException(nameof(value));
}
