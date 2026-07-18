using System.Globalization;
using System.Text.RegularExpressions;

namespace WinForge.Infrastructure;

public sealed record PowerSettingValue(Guid SubgroupGuid, Guid SettingGuid, int? AcValue, int? DcValue, string Name);

public static partial class PowerPlanParser
{
    public static IReadOnlyList<PowerSettingValue> Parse(string output)
    {
        var results = new List<PowerSettingValue>();
        Guid subgroup = Guid.Empty;
        Guid setting = Guid.Empty;
        string name = string.Empty;
        int? ac = null;
        int? dc = null;

        void Flush()
        {
            if (subgroup != Guid.Empty && setting != Guid.Empty) results.Add(new(subgroup, setting, ac, dc, name));
            setting = Guid.Empty; name = string.Empty; ac = null; dc = null;
        }

        foreach (var raw in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            var subgroupMatch = SubgroupRegex().Match(line);
            if (subgroupMatch.Success && Guid.TryParse(subgroupMatch.Groups[1].Value, out var parsedSubgroup)) { Flush(); subgroup = parsedSubgroup; continue; }
            var settingMatch = SettingRegex().Match(line);
            if (settingMatch.Success && Guid.TryParse(settingMatch.Groups[1].Value, out var parsedSetting)) { Flush(); setting = parsedSetting; name = settingMatch.Groups[2].Value.Trim(); continue; }
            var acMatch = AcRegex().Match(line);
            if (acMatch.Success) ac = ParseHex(acMatch.Groups[1].Value);
            var dcMatch = DcRegex().Match(line);
            if (dcMatch.Success) dc = ParseHex(dcMatch.Groups[1].Value);
        }
        Flush();
        return results;
    }

    private static int? ParseHex(string value) => int.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    [GeneratedRegex(@"Power Setting Subgroup GUID:\s*([0-9a-fA-F-]{36})", RegexOptions.IgnoreCase)] private static partial Regex SubgroupRegex();
    [GeneratedRegex(@"Power Setting GUID:\s*([0-9a-fA-F-]{36})\s*\((.*?)\)", RegexOptions.IgnoreCase)] private static partial Regex SettingRegex();
    [GeneratedRegex(@"Current AC Power Setting Index:\s*(0x[0-9a-fA-F]+)", RegexOptions.IgnoreCase)] private static partial Regex AcRegex();
    [GeneratedRegex(@"Current DC Power Setting Index:\s*(0x[0-9a-fA-F]+)", RegexOptions.IgnoreCase)] private static partial Regex DcRegex();
}
