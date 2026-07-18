using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WinForge.Core;

public sealed class CatalogEngine
{
    private static readonly HashSet<OperationKind> DestructiveKinds =
    [
        OperationKind.RegistryDeleteKey, OperationKind.AppxRemoveAllUsers,
        OperationKind.ProvisionedAppRemove, OperationKind.PowerSchemeDelete,
        OperationKind.FileDelete, OperationKind.DirectoryContentsDelete, OperationKind.RecycleBinClear
    ];

    public IReadOnlyList<ValidationIssue> Validate(CatalogDocument catalog, IReadOnlySet<string>? localizationKeys = null)
    {
        var issues = new List<ValidationIssue>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tweak in catalog.Tweaks)
        {
            if (string.IsNullOrWhiteSpace(tweak.Id) || !ids.Add(tweak.Id))
                issues.Add(new("duplicate-id", $"Duplicate or empty tweak id: {tweak.Id}", tweak.Id));
            if (tweak.Version < 1) issues.Add(new("version", "Version must be positive.", tweak.Id));
            if (tweak.LastReviewedDate > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
                issues.Add(new("review-date", "Review date cannot be in the future.", tweak.Id));
            if (!tweak.Reversible && tweak.Recommendation == Recommendation.Recommended)
                issues.Add(new("unsafe-recommendation", "Irreversible tweaks cannot be Recommended.", tweak.Id));
            if (tweak.ApplyOperations.Any(x => DestructiveKinds.Contains(x.Kind)) && !tweak.Destructive)
                issues.Add(new("destructive-flag", "Destructive operation requires destructive=true.", tweak.Id));
            if (tweak.Dependencies.Intersect(tweak.Conflicts, StringComparer.OrdinalIgnoreCase).Any())
                issues.Add(new("dependency-conflict", "A tweak cannot depend on and conflict with the same tweak.", tweak.Id));
            if (localizationKeys is not null)
            {
                foreach (var key in new[] { tweak.TitleKey, tweak.DescriptionKey, tweak.DetailedExplanationKey })
                    if (!localizationKeys.Contains(key)) issues.Add(new("localization", $"Missing localization key: {key}", tweak.Id));
            }
        }

        var known = ids;
        foreach (var profile in catalog.Profiles)
            foreach (var id in profile.TweakIds.Where(x => !known.Contains(x)))
                issues.Add(new("profile-reference", $"Profile {profile.Id} references unknown tweak {id}."));
        return issues;
    }

    public CompatibilityResult CheckCompatibility(TweakDefinition tweak, SystemSnapshot system)
    {
        var reasons = new List<string>();
        if (system.Build < ParseBuild(tweak.SupportedOperatingSystems.Minimum)) reasons.Add("Windows build is below the supported minimum.");
        if (tweak.SupportedOperatingSystems.Maximum is { } max && system.Build > ParseBuild(max)) reasons.Add("Windows build is above the reviewed maximum.");
        if (tweak.DesktopOnly && system.IsLaptop) reasons.Add("This tweak is desktop-only.");
        if (!tweak.LaptopAllowed && system.IsLaptop) reasons.Add("This tweak is disabled on laptops.");
        if (tweak.SupportedEditions.Count > 0 && !tweak.SupportedEditions.Contains(system.Edition, StringComparer.OrdinalIgnoreCase))
            reasons.Add("Windows edition is not supported.");
        return new(reasons.Count == 0, reasons);
    }

    public ExecutionPlan CreatePlan(CatalogDocument catalog, IEnumerable<string> selectedTweakIds, SystemSnapshot system, string catalogHash)
    {
        var byId = catalog.Tweaks.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<TweakDefinition>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(string id)
        {
            if (!byId.TryGetValue(id, out var tweak)) throw new InvalidOperationException($"Unknown tweak: {id}");
            if (added.Contains(id)) return;
            if (!visiting.Add(id)) throw new InvalidOperationException($"Dependency cycle at {id}");
            foreach (var dependency in tweak.Dependencies) Visit(dependency);
            visiting.Remove(id);
            if (!CheckCompatibility(tweak, system).Supported) throw new InvalidOperationException($"Unsupported tweak: {id}");
            if (tweak.Conflicts.Any(added.Contains)) throw new InvalidOperationException($"Tweak conflict: {id}");
            ordered.Add(tweak);
            added.Add(id);
        }

        foreach (var id in selectedTweakIds.Distinct(StringComparer.OrdinalIgnoreCase)) Visit(id);
        foreach (var tweak in ordered)
            if (tweak.Conflicts.Any(added.Contains)) throw new InvalidOperationException($"Tweak conflict: {tweak.Id}");

        return new(Guid.NewGuid(), DateTimeOffset.UtcNow, ordered.Select(x => new PlanItem(x, x.ApplyOperations)).ToArray(), catalogHash);
    }

    public static string ComputeHash(ReadOnlySpan<byte> content) => Convert.ToHexString(SHA256.HashData(content));

    private static int ParseBuild(string version)
    {
        var part = version.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var build) ? build : 0;
    }
}

public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
