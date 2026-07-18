using System.Text.Json;
using WinForge.Core;

namespace WinForge.Core.Tests;

public sealed class CatalogEngineTests
{
    private readonly CatalogEngine _engine = new();

    [Fact]
    public void Catalog_deserializes_typed_registry_operation()
    {
        var json = """{"schemaVersion":1,"tweaks":[{"id":"a","version":1,"titleKey":"t","descriptionKey":"d","detailedExplanationKey":"x","category":"Privacy","riskLevel":"low","recommendation":"recommended","requiresAdmin":false,"requiresRestart":false,"requiresExplorerRestart":false,"destructive":false,"reversible":true,"supportedOperatingSystems":{"minimum":"10.0.19045"},"expectedImpact":"clear","lastReviewedDate":"2026-01-01","applyOperations":[{"$type":"registry","id":"op","kind":"registrySetValue","requiresAdmin":false,"hive":"HKCU","keyPath":"Software\\Test","valueName":"x","value":0,"valueKind":"DWord"}]}],"profiles":[]}""";
        var catalog = JsonSerializer.Deserialize<CatalogDocument>(json, JsonDefaults.Options);
        Assert.NotNull(catalog);
        Assert.IsType<RegistryOperation>(catalog.Tweaks[0].ApplyOperations[0]);
    }

    [Fact]
    public void Duplicate_tweak_ids_are_rejected()
    {
        var catalog = new CatalogDocument(1, [Tweak("same"), Tweak("same")], []);
        Assert.Contains(_engine.Validate(catalog), x => x.Code == "duplicate-id");
    }

    [Fact]
    public void Missing_localization_keys_are_reported()
    {
        var issues = _engine.Validate(new(1, [Tweak("a")], []), new HashSet<string> { "title" });
        Assert.Equal(2, issues.Count(x => x.Code == "localization"));
    }

    [Fact]
    public void Irreversible_tweak_cannot_be_recommended()
    {
        var tweak = Tweak("a") with { Reversible = false, Recommendation = Recommendation.Recommended };
        Assert.Contains(_engine.Validate(new(1, [tweak], [])), x => x.Code == "unsafe-recommendation");
    }

    [Fact]
    public void Compatibility_filters_build_and_laptop_rules()
    {
        var tweak = Tweak("desktop") with { DesktopOnly = true, SupportedOperatingSystems = new("10.0.22631") };
        var result = _engine.CheckCompatibility(tweak, Snapshot(build: 19045, laptop: true));
        Assert.False(result.Supported);
        Assert.Equal(2, result.Reasons.Count);
    }

    [Fact]
    public void Profile_resolution_orders_dependency_first()
    {
        var dependency = Tweak("base");
        var selected = Tweak("selected") with { Dependencies = ["base"] };
        var plan = _engine.CreatePlan(new(1, [selected, dependency], []), ["selected"], Snapshot(), "00");
        Assert.Equal(["base", "selected"], plan.Items.Select(x => x.Tweak.Id));
    }

    [Fact]
    public void Conflicting_tweaks_fail_before_execution()
    {
        var first = Tweak("first") with { Conflicts = ["second"] };
        var second = Tweak("second");
        Assert.Throws<InvalidOperationException>(() => _engine.CreatePlan(new(1, [first, second], []), ["first", "second"], Snapshot(), "00"));
    }

    [Fact]
    public void Unsupported_tweak_fails_before_execution()
    {
        var tweak = Tweak("future") with { SupportedOperatingSystems = new("10.0.99999") };
        Assert.Throws<InvalidOperationException>(() => _engine.CreatePlan(new(1, [tweak], []), ["future"], Snapshot(), "00"));
    }

    internal static TweakDefinition Tweak(string id) => new()
    {
        Id = id,
        Version = 1,
        TitleKey = "title",
        DescriptionKey = "description",
        DetailedExplanationKey = "detail",
        Category = "Test",
        RiskLevel = RiskLevel.Low,
        Recommendation = Recommendation.Optional,
        Reversible = true,
        ExpectedImpact = "No claim",
        LastReviewedDate = new(2026, 1, 1)
    };

    internal static SystemSnapshot Snapshot(int build = 26100, bool laptop = false) => new("Windows", "Professional", build, laptop, "CPU", "GPU", 16L << 30, [], "Balanced", TimeSpan.Zero, 0, 0, 0, DateTimeOffset.UtcNow);
}
