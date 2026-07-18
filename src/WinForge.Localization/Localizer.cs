using System.Globalization;
using System.Resources;

namespace WinForge.Localization;

public sealed class Localizer
{
    private readonly ResourceManager _resources = new("WinForge.Localization.Strings", typeof(Localizer).Assembly);
    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("ar-SA");
    public bool IsRightToLeft => Culture.TextInfo.IsRightToLeft;
    public string this[string key] => _resources.GetString(key, Culture) ?? $"[{key}]";

    public void SetCulture(string cultureName)
    {
        Culture = CultureInfo.GetCultureInfo(cultureName is "ar-SA" or "en-US" ? cultureName : "en-US");
        CultureInfo.CurrentUICulture = Culture;
    }

    public IReadOnlySet<string> GetKnownKeys()
    {
        var set = _resources.GetResourceSet(CultureInfo.InvariantCulture, true, true);
        return set?.Cast<System.Collections.DictionaryEntry>().Select(x => x.Key?.ToString() ?? string.Empty).ToHashSet(StringComparer.Ordinal) ?? [];
    }
}
