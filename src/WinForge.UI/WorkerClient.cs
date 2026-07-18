using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using WinForge.Core;

namespace WinForge.UI;

public sealed class WorkerClient
{
    public async Task<WorkerResponse> ExecuteAsync(ExecutionPlan plan, JournalSession? rollbackSession = null, CancellationToken cancellationToken = default)
    {
        var pipeName = "WinForge-" + Guid.NewGuid().ToString("N");
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var workerPath = Path.Combine(AppContext.BaseDirectory, "WinForge.Worker.exe");
        if (!File.Exists(workerPath)) return new(false, "WinForge.Worker.exe was not found beside the UI executable.");
        using var current = Process.GetCurrentProcess();
        var requiresAdmin = plan.Items.SelectMany(x => x.Operations).Any(x => x.RequiresAdmin);
        var startInfo = new ProcessStartInfo { FileName = workerPath, UseShellExecute = true };
        if (requiresAdmin) startInfo.Verb = "runas";
        foreach (var argument in new[] { "--pipe", pipeName, "--nonce", nonce, "--ui-pid", current.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) }) startInfo.ArgumentList.Add(argument);
        try
        {
            using var worker = Process.Start(startInfo);
            if (worker is null) return new(false, "Worker could not be started.");
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(60));
            await pipe.ConnectAsync(timeout.Token);
            await JsonSerializer.SerializeAsync(pipe, new WorkerRequest(nonce, current.Id, plan, rollbackSession is not null, rollbackSession), JsonDefaults.Options, timeout.Token); await pipe.FlushAsync(timeout.Token);
            return await JsonSerializer.DeserializeAsync<WorkerResponse>(pipe, JsonDefaults.Options, timeout.Token) ?? new(false, "Worker returned an empty response.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return new(false, "Administrator approval was cancelled."); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException) { return new(false, ex.Message); }
    }
}
