using System.Text.RegularExpressions;

namespace WinForge.Infrastructure;

public sealed record PowerScheme(Guid Guid, string Name, bool Active);

public sealed partial class PowerPlanManager(SafeProcessRunner runner)
{
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string WinForgePlanName = "WinForge Balanced Gaming";

    public async Task<IReadOnlyList<PowerScheme>> GetSchemesAsync(CancellationToken cancellationToken = default)
    {
        var result = await runner.RunAsync("powercfg.exe", ["/list"], TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) return [];
        return SchemeRegex().Matches(result.StandardOutput).Select(match => new PowerScheme(
            Guid.Parse(match.Groups[1].Value), match.Groups[2].Value.Trim(), match.Groups[3].Value == "*")).ToArray();
    }

    public async Task<Guid> EnsureGamingPlanAsync(CancellationToken cancellationToken = default)
    {
        var existing = FindManagedPlan(await GetSchemesAsync(cancellationToken).ConfigureAwait(false));
        if (existing is not null) return existing.Guid;
        var duplicate = await runner.RunAsync("powercfg.exe", ["/duplicatescheme", BalancedGuid], TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
        if (duplicate.ExitCode != 0) throw new InvalidOperationException(duplicate.StandardError);
        var match = GuidRegex().Match(duplicate.StandardOutput);
        if (!match.Success) throw new InvalidOperationException("powercfg did not return the duplicated scheme GUID.");
        var guid = Guid.Parse(match.Value);
        var rename = await runner.RunAsync("powercfg.exe", ["/changename", guid.ToString("D"), WinForgePlanName, "Conservative gaming plan managed by WinForge"], TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        if (rename.ExitCode != 0) throw new InvalidOperationException(rename.StandardError);
        return guid;
    }

    public Task<ProcessRunResult> ActivateAsync(Guid scheme, CancellationToken cancellationToken = default) => runner.RunAsync("powercfg.exe", ["/setactive", scheme.ToString("D")], TimeSpan.FromSeconds(10), cancellationToken);
    public Task<ProcessRunResult> RestoreBalancedAsync(CancellationToken cancellationToken = default) => runner.RunAsync("powercfg.exe", ["/setactive", BalancedGuid], TimeSpan.FromSeconds(10), cancellationToken);
    public static PowerScheme? FindManagedPlan(IEnumerable<PowerScheme> schemes) => schemes.FirstOrDefault(x => string.Equals(x.Name, WinForgePlanName, StringComparison.OrdinalIgnoreCase));

    public async Task<bool> DeleteWinForgePlanAsync(CancellationToken cancellationToken = default)
    {
        var scheme = (await GetSchemesAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(x => string.Equals(x.Name, WinForgePlanName, StringComparison.OrdinalIgnoreCase));
        if (scheme is null) return true;
        if (scheme.Active) await RestoreBalancedAsync(cancellationToken).ConfigureAwait(false);
        var result = await runner.RunAsync("powercfg.exe", ["/delete", scheme.Guid.ToString("D")], TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 && !(await GetSchemesAsync(cancellationToken).ConfigureAwait(false)).Any(x => x.Guid == scheme.Guid);
    }

    public async Task<IReadOnlyList<PowerSettingValue>> InspectAsync(Guid scheme, CancellationToken cancellationToken = default)
    {
        var result = await runner.RunAsync("powercfg.exe", ["/query", scheme.ToString("D")], TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 ? PowerPlanParser.Parse(result.StandardOutput) : [];
    }

    [GeneratedRegex(@"Power Scheme GUID:\s*([0-9a-fA-F-]{36})\s*\((.*?)\)\s*(\*)?", RegexOptions.IgnoreCase)] private static partial Regex SchemeRegex();
    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")] private static partial Regex GuidRegex();
}
