namespace WinForge.Core;

public sealed class ExecutionEngine(IOperationExecutor executor, IJournalStore journal)
{
    public async Task<JournalSession> ExecuteAsync(ExecutionPlan plan, CancellationToken cancellationToken = default)
    {
        await journal.WritePlanAsync(plan, cancellationToken).ConfigureAwait(false);
        var states = new List<OperationBeforeState>();
        var results = new List<OperationResult>();
        var session = new JournalSession(plan.SessionId, DateTimeOffset.UtcNow, states, results);
        await journal.WriteSessionAsync(session, cancellationToken).ConfigureAwait(false);

        foreach (var operation in plan.Items.SelectMany(x => x.Operations))
        {
            var state = await executor.CaptureBeforeAsync(operation, cancellationToken).ConfigureAwait(false);
            states.Add(state);
            await journal.WriteSessionAsync(session with { BeforeStates = states.ToArray(), Results = results.ToArray() }, cancellationToken).ConfigureAwait(false);
            var result = await executor.ExecuteAsync(operation, cancellationToken).ConfigureAwait(false);
            results.Add(result);
            await journal.WriteSessionAsync(session with { BeforeStates = states.ToArray(), Results = results.ToArray() }, cancellationToken).ConfigureAwait(false);
            if (!result.Success || !result.Verified)
            {
                await RollbackCompletedOperationsAsync(plan, states, results, cancellationToken).ConfigureAwait(false);
                return session with { BeforeStates = states.ToArray(), Results = results.ToArray(), RollbackCompleted = true };
            }
        }

        return session with { BeforeStates = states.ToArray(), Results = results.ToArray() };
    }

    public async Task<JournalSession> RollbackAsync(ExecutionPlan plan, JournalSession session, CancellationToken cancellationToken = default)
    {
        var operations = plan.Items.SelectMany(x => x.Operations).ToArray();
        var rollbackResults = new List<OperationResult>();
        for (var index = Math.Min(operations.Length, session.BeforeStates.Count) - 1; index >= 0; index--)
            rollbackResults.Add(await executor.RollbackAsync(operations[index], session.BeforeStates[index], cancellationToken).ConfigureAwait(false));
        var completed = rollbackResults.All(x => x.Success && x.Verified);
        var updated = session with { Results = session.Results.Concat(rollbackResults).ToArray(), RollbackCompleted = completed };
        await journal.WriteSessionAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    private async Task RollbackCompletedOperationsAsync(ExecutionPlan plan, IReadOnlyList<OperationBeforeState> states, List<OperationResult> results, CancellationToken cancellationToken)
    {
        var operations = plan.Items.SelectMany(x => x.Operations).Take(states.Count).ToArray();
        for (var index = operations.Length - 1; index >= 0; index--)
            results.Add(await executor.RollbackAsync(operations[index], states[index], cancellationToken).ConfigureAwait(false));
    }
}
