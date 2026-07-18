using System.Text.Json;
using WinForge.Core;
using WinForge.Infrastructure;

namespace WinForge.Infrastructure.Tests;

public sealed class InfrastructureSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WinForgeTests", Guid.NewGuid().ToString("N"));

    public InfrastructureSafetyTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Catalog_file_passes_schema_and_domain_validation()
    {
        var root = FindSolutionRoot();
        using var schema = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "catalog", "schemas", "catalog.schema.json")));
        Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
        var loaded = await new CatalogRepository().LoadAsync(Path.Combine(root, "catalog", "tweaks", "core.json"));
        Assert.NotEmpty(loaded.Catalog.Tweaks);
        Assert.Equal(64, loaded.Hash.Length);
    }

    [Fact]
    public async Task Catalog_schema_rejects_missing_required_properties()
    {
        var catalogDirectory = Path.Combine(_root, "catalog", "tweaks");
        var schemaDirectory = Path.Combine(_root, "catalog", "schemas");
        Directory.CreateDirectory(catalogDirectory); Directory.CreateDirectory(schemaDirectory);
        File.Copy(Path.Combine(FindSolutionRoot(), "catalog", "schemas", "catalog.schema.json"), Path.Combine(schemaDirectory, "catalog.schema.json"));
        var path = Path.Combine(catalogDirectory, "invalid.json");
        await File.WriteAllTextAsync(path, "{\"schemaVersion\":1,\"tweaks\":[{}],\"profiles\":[]}");
        await Assert.ThrowsAsync<InvalidDataException>(() => new CatalogRepository().LoadAsync(path));
    }

    [Fact]
    public async Task Cleaner_preview_and_clean_are_confined_to_allowlisted_fixture()
    {
        var allowed = Path.Combine(_root, "allowed"); Directory.CreateDirectory(allowed);
        var file = Path.Combine(allowed, "cache.tmp"); await File.WriteAllTextAsync(file, "fixture");
        var cleaner = new SafeCleaner([new("fixture", [allowed], true, "test fixture")]);
        var preview = Assert.Single(await cleaner.PreviewAsync(["fixture"]));
        Assert.Equal(1, preview.FileCount);
        var result = Assert.Single(await cleaner.CleanAsync([preview]));
        Assert.Equal(1, result.DeletedFiles);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Cleaner_blocks_path_traversal()
    {
        var allowed = Path.Combine(_root, "allowed"); var outside = Path.Combine(_root, "outside.tmp"); Directory.CreateDirectory(allowed); File.WriteAllText(outside, "fixture");
        var cleaner = new SafeCleaner([new("fixture", [allowed], true, "test fixture")]);
        Assert.False(cleaner.IsPathAllowed("fixture", Path.Combine(allowed, "..", "outside.tmp")));
    }

    [Fact]
    public async Task Cleaner_file_patterns_exclude_unrelated_cache_files()
    {
        var allowed = Path.Combine(_root, "explorer"); Directory.CreateDirectory(allowed);
        await File.WriteAllTextAsync(Path.Combine(allowed, "thumbcache_256.db"), "thumbnail");
        await File.WriteAllTextAsync(Path.Combine(allowed, "iconcache_256.db"), "icon");
        var cleaner = new SafeCleaner([new("thumbnails", [allowed], false, "fixture", default, ["thumbcache*.db"])]);
        var preview = Assert.Single(await cleaner.PreviewAsync(["thumbnails"]));
        Assert.Single(preview.Items); Assert.Contains("thumbcache", preview.Items[0].Path);
    }

    [Fact]
    public void Cleaner_does_not_follow_reparse_points_when_supported()
    {
        var allowed = Path.Combine(_root, "allowed"); var outside = Path.Combine(_root, "outside"); var link = Path.Combine(allowed, "link"); Directory.CreateDirectory(allowed); Directory.CreateDirectory(outside);
        try { Directory.CreateSymbolicLink(link, outside); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return; }
        var cleaner = new SafeCleaner([new("fixture", [allowed], true, "test fixture")]);
        Assert.False(cleaner.IsPathAllowed("fixture", Path.Combine(link, "secret.tmp")));
    }

    [Fact]
    public async Task Process_runner_rejects_non_allowlisted_executable_before_start()
    {
        var runner = new SafeProcessRunner();
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync("cmd.exe", ["/c", "echo unsafe"], TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Process_runner_rejects_control_characters()
    {
        var runner = new SafeProcessRunner();
        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync("powercfg.exe", ["/list\nwhoami"], TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Power_plan_parser_extracts_ac_and_dc_values()
    {
        var text = """
          Power Setting Subgroup GUID: 54533251-82be-4824-96c1-47b60b740d00  (Processor power management)
            Power Setting GUID: 893dee8e-2bef-41e0-89c6-b55d0929964c  (Minimum processor state)
              Current AC Power Setting Index: 0x00000005
              Current DC Power Setting Index: 0x0000000a
          """;
        var setting = Assert.Single(PowerPlanParser.Parse(text));
        Assert.Equal(5, setting.AcValue); Assert.Equal(10, setting.DcValue);
    }

    [Fact]
    public void Managed_power_plan_detection_prevents_duplicates()
    {
        var existing = new PowerScheme(Guid.NewGuid(), PowerPlanManager.WinForgePlanName, false);
        Assert.Same(existing, PowerPlanManager.FindManagedPlan([new(Guid.NewGuid(), "Balanced", true), existing]));
    }

    [Fact]
    public async Task Journal_is_written_atomically_and_preserves_absence()
    {
        var store = new JournalStore(Path.Combine(_root, "journal")); var id = Guid.NewGuid();
        var session = new JournalSession(id, DateTimeOffset.UtcNow, [new("reg", false, null, null)], []);
        await store.WriteSessionAsync(session);
        var copy = await store.ReadSessionAsync(id);
        Assert.NotNull(copy); Assert.False(copy.BeforeStates[0].Existed); Assert.False(File.Exists(Path.Combine(store.RootPath, id.ToString("D"), "session.json.tmp")));
    }

    [Theory]
    [InlineData("DWord", "42", typeof(int))]
    [InlineData("QWord", "922337203685477", typeof(long))]
    [InlineData("String", "\"value\"", typeof(string))]
    public void Journal_registry_values_restore_their_runtime_type(string valueType, string json, Type expectedType)
    {
        using var document = JsonDocument.Parse(json);
        var restored = WindowsOperationExecutor.NormalizeStoredValue(document.RootElement.Clone(), valueType);
        Assert.IsType(expectedType, restored);
    }

    [Fact]
    public void Journal_binary_registry_value_restores_byte_array()
    {
        using var document = JsonDocument.Parse("\"AQID\"");
        Assert.Equal([1, 2, 3], Assert.IsType<byte[]>(WindowsOperationExecutor.NormalizeStoredValue(document.RootElement.Clone(), "Binary")));
    }

    [Fact]
    public async Task Invalid_service_identifier_fails_without_starting_a_process()
    {
        var executor = new WindowsOperationExecutor(new SafeProcessRunner());
        var result = await executor.ExecuteAsync(new ServiceOperation("bad", OperationKind.ServiceStop, true, "Bad & Dangerous"));
        Assert.False(result.Success); Assert.Contains("Invalid service", result.Message);
    }

    [Theory]
    [InlineData(OperationKind.ServiceStop, null)]
    [InlineData(OperationKind.ServiceSetStartupType, "disabled")]
    public async Task Protected_service_stop_or_disable_is_blocked_before_process_start(OperationKind kind, string? startupType)
    {
        var executor = new WindowsOperationExecutor(new SafeProcessRunner());
        var result = await executor.ExecuteAsync(new ServiceOperation("protected", kind, true, "WinDefend", startupType));
        Assert.False(result.Success); Assert.Contains("Protected service", result.Message);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WinForge.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Solution root not found.");
    }
}
