using System.IO.Compression;
using System.Text.Json;
using WinForge.Core;

namespace WinForge.Infrastructure;

public sealed class JournalStore : IJournalStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public string RootPath { get; }

    public JournalStore(string? rootPath = null)
    {
        RootPath = rootPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinForge", "Rollback");
    }

    public async Task WritePlanAsync(ExecutionPlan plan, CancellationToken cancellationToken = default)
    {
        var directory = SessionDirectory(plan.SessionId);
        Directory.CreateDirectory(directory);
        await WriteAtomicAsync(Path.Combine(directory, "operations.json"), plan, cancellationToken).ConfigureAwait(false);
        var summary = $"WinForge session {plan.SessionId}{Environment.NewLine}Created: {plan.CreatedAt:O}{Environment.NewLine}Tweaks: {string.Join(", ", plan.Items.Select(x => x.Tweak.Id))}";
        await File.WriteAllTextAsync(Path.Combine(directory, "summary.txt"), summary, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(directory, "rollback.ps1"), "# Emergency rollback is generated from before-state.json by WinForge. Open WinForge > Rollback to restore safely.", cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteSessionAsync(JournalSession session, CancellationToken cancellationToken = default)
    {
        var directory = SessionDirectory(session.SessionId);
        Directory.CreateDirectory(directory);
        await WriteAtomicAsync(Path.Combine(directory, "session.json"), session, cancellationToken).ConfigureAwait(false);
        await WriteAtomicAsync(Path.Combine(directory, "before-state.json"), session.BeforeStates, cancellationToken).ConfigureAwait(false);
        var logLines = session.Results.Select(x => JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.UtcNow,
            sessionId = session.SessionId,
            operationId = x.OperationId,
            stage = "execute",
            result = x.Success && x.Verified ? "success" : "failure",
            x.ExitCode,
            durationMs = x.Duration.TotalMilliseconds,
            message = Sanitize(x.Message)
        }, JsonDefaults.Options));
        await File.WriteAllLinesAsync(Path.Combine(directory, "execution.log"), logLines, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JournalSession?> ReadSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(SessionDirectory(sessionId), "session.json");
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<JournalSession>(stream, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, JsonDefaults.Options), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, true);
        }
        finally { _gate.Release(); }
    }

    private string SessionDirectory(Guid id) => Path.Combine(RootPath, id.ToString("D"));
    private static string Sanitize(string value)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(profile) ? value : value.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class SupportBundleCreator(JournalStore journal) : ISupportBundleCreator
{
    public async Task<string> CreateAsync(SystemSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var supportRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinForge", "Support");
        Directory.CreateDirectory(supportRoot);
        var work = Path.Combine(Path.GetTempPath(), "WinForgeSupport", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(work, "system.json"), JsonSerializer.Serialize(snapshot, JsonDefaults.Options), cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(work, "version.txt"), typeof(SupportBundleCreator).Assembly.GetName().Version?.ToString() ?? "unknown", cancellationToken).ConfigureAwait(false);
            if (Directory.Exists(journal.RootPath))
            {
                foreach (var file in Directory.EnumerateFiles(journal.RootPath, "execution.log", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).Take(5))
                    File.Copy(file, Path.Combine(work, $"log-{Guid.NewGuid():N}.jsonl"));
            }
            var output = Path.Combine(supportRoot, $"WinForge-Support-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");
            ZipFile.CreateFromDirectory(work, output, CompressionLevel.Optimal, false);
            return output;
        }
        finally { try { Directory.Delete(work, true); } catch (IOException) { } }
    }
}
