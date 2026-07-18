namespace WinForge.Core;

public interface ISystemScanner { Task<SystemSnapshot> ScanAsync(CancellationToken cancellationToken = default); }
public interface ICleaner
{
    Task<IReadOnlyList<CleanerPreview>> PreviewAsync(IReadOnlyCollection<string> categories, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CleanerResult>> CleanAsync(IReadOnlyCollection<CleanerPreview> previews, CancellationToken cancellationToken = default);
}
public interface IStartupReader { Task<IReadOnlyList<StartupEntry>> GetEntriesAsync(CancellationToken cancellationToken = default); }
public interface IServiceReader { Task<IReadOnlyList<ServiceEntry>> GetEntriesAsync(CancellationToken cancellationToken = default); }
public interface IScheduledTaskReader { Task<IReadOnlyList<ScheduledTaskEntry>> GetEntriesAsync(CancellationToken cancellationToken = default); }
public interface IAppxReader { Task<IReadOnlyList<AppxEntry>> GetEntriesAsync(CancellationToken cancellationToken = default); }
public interface IOptionalFeatureReader { Task<IReadOnlyList<OptionalFeatureEntry>> GetEntriesAsync(CancellationToken cancellationToken = default); }
public interface INetworkReader { Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync(CancellationToken cancellationToken = default); }
public interface IOperationExecutor
{
    Task<OperationBeforeState> CaptureBeforeAsync(TypedOperation operation, CancellationToken cancellationToken = default);
    Task<OperationResult> ExecuteAsync(TypedOperation operation, CancellationToken cancellationToken = default);
    Task<OperationResult> RollbackAsync(TypedOperation operation, OperationBeforeState beforeState, CancellationToken cancellationToken = default);
}
public interface IJournalStore
{
    string RootPath { get; }
    Task WritePlanAsync(ExecutionPlan plan, CancellationToken cancellationToken = default);
    Task WriteSessionAsync(JournalSession session, CancellationToken cancellationToken = default);
    Task<JournalSession?> ReadSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
public interface ISupportBundleCreator { Task<string> CreateAsync(SystemSnapshot snapshot, CancellationToken cancellationToken = default); }
