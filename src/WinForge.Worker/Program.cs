using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using WinForge.Core;
using WinForge.Infrastructure;

return await WorkerProgram.RunAsync(args);

internal static class WorkerProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = ParseArgs(args);
        if (!options.TryGetValue("pipe", out var pipeName) || !options.TryGetValue("nonce", out var nonce) ||
            !options.TryGetValue("ui-pid", out var pidText) || !int.TryParse(pidText, out var uiPid) || !ValidPipeName(pipeName)) return 2;
        try
        {
            using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await pipe.WaitForConnectionAsync(timeout.Token);
            if (!ValidateClient(pipe, uiPid)) return await WriteAsync(pipe, new(false, "Worker rejected the connecting process."), timeout.Token);
            var request = await JsonSerializer.DeserializeAsync<WorkerRequest>(pipe, JsonDefaults.Options, timeout.Token);
            if (request is null || request.Nonce != nonce || request.UiProcessId != uiPid) return await WriteAsync(pipe, new(false, "Worker request authentication failed."), timeout.Token);
            var catalogPath = Path.Combine(AppContext.BaseDirectory, "catalog", "tweaks", "core.json");
            var catalog = await new CatalogRepository().LoadAsync(catalogPath, null, timeout.Token);
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(catalog.Hash), Convert.FromHexString(request.Plan.CatalogHash)))
                return await WriteAsync(pipe, new(false, "Catalog integrity check failed."), timeout.Token);
            var allowedTweaks = catalog.Catalog.Tweaks.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
            if (!PlanMatchesCatalog(request.Plan, allowedTweaks)) return await WriteAsync(pipe, new(false, "Execution plan does not match the validated catalog."), timeout.Token);
            var journal = new JournalStore();
            var engine = new ExecutionEngine(new WindowsOperationExecutor(new SafeProcessRunner()), journal);
            var session = request.Rollback && request.Session is not null
                ? await engine.RollbackAsync(request.Plan, request.Session, timeout.Token)
                : await engine.ExecuteAsync(request.Plan, timeout.Token);
            var success = session.Results.All(x => x.Success && x.Verified);
            return await WriteAsync(pipe, new(success, success ? (request.Rollback ? "Rollback completed and verified." : "Execution completed and verified.") : "Operation failed verification.", session), timeout.Token);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or OperationCanceledException or UnauthorizedAccessException)
        {
            return 1;
        }
    }

    private static bool PlanMatchesCatalog(ExecutionPlan plan, IReadOnlyDictionary<string, TweakDefinition> catalog)
    {
        foreach (var item in plan.Items)
        {
            if (!catalog.TryGetValue(item.Tweak.Id, out var definition)) return false;
            var expected = JsonSerializer.Serialize(definition.ApplyOperations, JsonDefaults.Options);
            var actual = JsonSerializer.Serialize(item.Operations, JsonDefaults.Options);
            if (!string.Equals(expected, actual, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static bool ValidateClient(NamedPipeServerStream pipe, int expectedPid)
    {
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var actualPid) || actualPid != (uint)expectedPid) return false;
        try
        {
            using var process = Process.GetProcessById(expectedPid);
            var path = process.MainModule?.FileName;
            return path is not null && string.Equals(Path.GetFileName(path), "WinForge.UI.exe", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(Path.GetDirectoryName(path), AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    private static async Task<int> WriteAsync(NamedPipeServerStream pipe, WorkerResponse response, CancellationToken cancellationToken)
    {
        await JsonSerializer.SerializeAsync(pipe, response, JsonDefaults.Options, cancellationToken); await pipe.FlushAsync(cancellationToken); return response.Success ? 0 : 1;
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i + 1 < args.Length; i += 2) if (args[i].StartsWith("--", StringComparison.Ordinal)) result[args[i][2..]] = args[i + 1];
        return result;
    }
    private static bool ValidPipeName(string value) => value.Length is > 8 and < 100 && value.All(x => char.IsLetterOrDigit(x) || x == '-');

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeClientProcessId(nint pipe, out uint clientProcessId);
}
