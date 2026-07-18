using System.Text.Json.Serialization;

namespace WinForge.Core;

public enum RiskLevel { Low, Medium, High, Expert }
public enum Recommendation { Recommended, Optional, NotRecommended }
public enum OperationKind
{
    RegistrySetValue, RegistryDeleteValue, RegistryCreateKey, RegistryDeleteKey,
    ServiceSetStartupType, ServiceStart, ServiceStop,
    ScheduledTaskEnable, ScheduledTaskDisable,
    OptionalFeatureEnable, OptionalFeatureDisable,
    AppxRemoveCurrentUser, AppxRemoveAllUsers, ProvisionedAppRemove,
    PowerSchemeDuplicate, PowerSchemeRename, PowerSchemeActivate, PowerSchemeDelete,
    PowerSettingSetAc, PowerSettingSetDc,
    FileDelete, DirectoryContentsDelete, RecycleBinClear,
    StartupRegistryEnable, StartupRegistryDisable,
    StartupShortcutEnable, StartupShortcutDisable,
    ExplorerRestart, Command
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(RegistryOperation), "registry")]
[JsonDerivedType(typeof(ServiceOperation), "service")]
[JsonDerivedType(typeof(ScheduledTaskOperation), "task")]
[JsonDerivedType(typeof(OptionalFeatureOperation), "feature")]
[JsonDerivedType(typeof(AppxOperation), "appx")]
[JsonDerivedType(typeof(PowerOperation), "power")]
[JsonDerivedType(typeof(FileOperation), "file")]
[JsonDerivedType(typeof(StartupOperation), "startup")]
[JsonDerivedType(typeof(CommandOperation), "command")]
public abstract record TypedOperation(string Id, OperationKind Kind, bool RequiresAdmin);

public sealed record RegistryOperation(
    string Id, OperationKind Kind, bool RequiresAdmin, string Hive, string KeyPath,
    string? ValueName = null, object? Value = null, string? ValueKind = null) : TypedOperation(Id, Kind, RequiresAdmin);

public sealed record ServiceOperation(
    string Id, OperationKind Kind, bool RequiresAdmin, string ServiceName, string? StartupType = null) : TypedOperation(Id, Kind, RequiresAdmin);

public sealed record ScheduledTaskOperation(
    string Id, OperationKind Kind, bool RequiresAdmin, string TaskPath, string TaskName) : TypedOperation(Id, Kind, RequiresAdmin);

public sealed record OptionalFeatureOperation(
    string Id, OperationKind Kind, bool RequiresAdmin, string FeatureName) : TypedOperation(Id, Kind, RequiresAdmin);

public sealed record AppxOperation(
    string Id, OperationKind Kind, bool RequiresAdmin, string PackageName) : TypedOperation(Id, Kind, RequiresAdmin);

public sealed record PowerOperation(
    string Id, OperationKind Kind, bool RequiresAdmin, string? SchemeGuid = null,
    string? SubgroupGuid = null, string? SettingGuid = null, int? Value = null, string? Name = null) : TypedOperation(Id, Kind, RequiresAdmin);

public sealed record FileOperation(
    string Id, OperationKind Kind, bool RequiresAdmin, string Path, string? AllowlistedRoot = null) : TypedOperation(Id, Kind, RequiresAdmin);

public sealed record StartupOperation(
    string Id, OperationKind Kind, bool RequiresAdmin, string Source, string Name, string? OriginalCommand = null) : TypedOperation(Id, Kind, RequiresAdmin);

public sealed record CommandOperation(
    string Id, OperationKind Kind, bool RequiresAdmin, string CommandId, IReadOnlyList<string> Arguments) : TypedOperation(Id, Kind, RequiresAdmin);

public sealed record OsSupport(string Minimum = "10.0.19045", string? Maximum = null);

public sealed record TweakDefinition
{
    public required string Id { get; init; }
    public required int Version { get; init; }
    public required string TitleKey { get; init; }
    public required string DescriptionKey { get; init; }
    public required string DetailedExplanationKey { get; init; }
    public required string Category { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public RiskLevel RiskLevel { get; init; }
    public Recommendation Recommendation { get; init; }
    public bool RequiresAdmin { get; init; }
    public bool RequiresRestart { get; init; }
    public bool RequiresExplorerRestart { get; init; }
    public bool Destructive { get; init; }
    public bool Reversible { get; init; }
    public OsSupport SupportedOperatingSystems { get; init; } = new();
    public IReadOnlyList<string> SupportedEditions { get; init; } = [];
    public bool DesktopOnly { get; init; }
    public bool LaptopAllowed { get; init; } = true;
    public IReadOnlyList<string> Dependencies { get; init; } = [];
    public IReadOnlyList<string> Conflicts { get; init; } = [];
    public IReadOnlyList<TypedOperation> DetectOperations { get; init; } = [];
    public IReadOnlyList<TypedOperation> ApplyOperations { get; init; } = [];
    public IReadOnlyList<TypedOperation> RollbackOperations { get; init; } = [];
    public IReadOnlyList<TypedOperation> VerificationOperations { get; init; } = [];
    public required string ExpectedImpact { get; init; }
    public IReadOnlyList<string> PossibleSideEffects { get; init; } = [];
    public IReadOnlyList<string> EvidenceReferences { get; init; } = [];
    public required DateOnly LastReviewedDate { get; init; }
}

public sealed record ProfileDefinition(string Id, string TitleKey, IReadOnlyList<string> TweakIds, bool DesktopOnly = false);
public sealed record CatalogDocument(int SchemaVersion, IReadOnlyList<TweakDefinition> Tweaks, IReadOnlyList<ProfileDefinition> Profiles);

public sealed record SystemSnapshot(
    string WindowsVersion, string Edition, int Build, bool IsLaptop, string Cpu, string Gpu,
    long MemoryBytes, IReadOnlyList<DriveSnapshot> Drives, string ActivePowerScheme, TimeSpan Uptime,
    int StartupCount, int ProcessCount, int ScheduledTaskCount, DateTimeOffset ScannedAt);

public sealed record DriveSnapshot(string Name, string Format, long TotalBytes, long FreeBytes, string MediaType);
public sealed record CompatibilityResult(bool Supported, IReadOnlyList<string> Reasons);
public sealed record PlanItem(TweakDefinition Tweak, IReadOnlyList<TypedOperation> Operations);
public sealed record ExecutionPlan(Guid SessionId, DateTimeOffset CreatedAt, IReadOnlyList<PlanItem> Items, string CatalogHash);
public sealed record ValidationIssue(string Code, string Message, string? TweakId = null);

public sealed record OperationBeforeState(string OperationId, bool Existed, string? ValueType, object? Value, IReadOnlyDictionary<string, string>? Metadata = null);
public sealed record OperationResult(string OperationId, bool Success, bool Verified, int? ExitCode, string Message, TimeSpan Duration);
public sealed record JournalSession(Guid SessionId, DateTimeOffset CreatedAt, IReadOnlyList<OperationBeforeState> BeforeStates, IReadOnlyList<OperationResult> Results, bool RollbackCompleted = false);

public sealed record CleanerItem(string Path, long Length, string Category);
public sealed record CleanerPreview(string Category, int FileCount, long TotalBytes, IReadOnlyList<CleanerItem> Items, IReadOnlyList<string> Errors);
public sealed record CleanerResult(string Category, int DeletedFiles, long DeletedBytes, IReadOnlyList<string> Errors);

public sealed record StartupEntry(string Name, string Command, string Source, bool Enabled, string Publisher, string? Path);
public sealed record ServiceEntry(string Name, string DisplayName, string Status, string StartupType, int? ProcessId, string Path, string Classification);
public sealed record ScheduledTaskEntry(string Path, string Name, string State, IReadOnlyList<string> Triggers, IReadOnlyList<string> Actions);
public sealed record AppxEntry(string Name, string PackageFullName, string Publisher, string Version, bool Provisioned, string Classification);
public sealed record OptionalFeatureEntry(string Name, string State, string Classification);
public sealed record NetworkAdapterInfo(string Name, string Status, long LinkSpeed, string Dns, int? Mtu, bool DhcpEnabled);
public sealed record WorkerRequest(string Nonce, int UiProcessId, ExecutionPlan Plan, Guid? RollbackSessionId = null);
public sealed record WorkerResponse(bool Success, string Message, JournalSession? Session = null);
