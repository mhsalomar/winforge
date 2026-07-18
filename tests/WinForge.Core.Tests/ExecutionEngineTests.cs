using System.Text.Json;
using WinForge.Core;

namespace WinForge.Core.Tests;

public sealed class ExecutionEngineTests
{
    [Fact]
    public void Rollback_journal_round_trips_absent_before_state()
    {
        var session = new JournalSession(Guid.NewGuid(), DateTimeOffset.UtcNow, [new("op", false, null, null)], []);
        var copy = JsonSerializer.Deserialize<JournalSession>(JsonSerializer.Serialize(session, JsonDefaults.Options), JsonDefaults.Options);
        Assert.NotNull(copy);
        Assert.False(copy.BeforeStates[0].Existed);
        Assert.Null(copy.BeforeStates[0].Value);
    }

    [Fact]
    public async Task Partial_batch_failure_rolls_back_in_reverse_order()
    {
        var executor = new FakeExecutor(failOn: "op-2");
        var journal = new FakeJournal();
        var tweak = CatalogEngineTests.Tweak("batch") with
        {
            ApplyOperations =
            [
                new RegistryOperation("op-1", OperationKind.RegistrySetValue, false, "HKCU", @"Software\Test", "A", 1, "DWord"),
                new RegistryOperation("op-2", OperationKind.RegistrySetValue, false, "HKCU", @"Software\Test", "B", 1, "DWord")
            ]
        };
        var plan = new ExecutionPlan(Guid.NewGuid(), DateTimeOffset.UtcNow, [new(tweak, tweak.ApplyOperations)], "00");
        var result = await new ExecutionEngine(executor, journal).ExecuteAsync(plan);
        Assert.True(result.RollbackCompleted);
        Assert.Equal(["op-2", "op-1"], executor.RolledBack);
        Assert.True(journal.PlanWrittenBeforeSession);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "Manual")]
    public async Task Before_state_is_preserved_for_service_restore(bool existed, string? startupType)
    {
        var state = new OperationBeforeState("svc", existed, null, null, startupType is null ? null : new Dictionary<string, string> { ["startupType"] = startupType });
        var executor = new FakeExecutor(before: state);
        var op = new ServiceOperation("svc", OperationKind.ServiceSetStartupType, true, "Example", "disabled");
        var tweak = CatalogEngineTests.Tweak("service") with { ApplyOperations = [op] };
        var plan = new ExecutionPlan(Guid.NewGuid(), DateTimeOffset.UtcNow, [new(tweak, [op])], "00");
        var session = await new ExecutionEngine(executor, new FakeJournal()).ExecuteAsync(plan);
        Assert.Equal(existed, session.BeforeStates[0].Existed);
        Assert.Equal(startupType, session.BeforeStates[0].Metadata?.GetValueOrDefault("startupType"));
    }

    [Fact]
    public async Task Scheduled_task_state_is_preserved()
    {
        var before = new OperationBeforeState("task", true, null, null, new Dictionary<string, string> { ["enabled"] = "true" });
        var executor = new FakeExecutor(before: before);
        var op = new ScheduledTaskOperation("task", OperationKind.ScheduledTaskDisable, true, @"\Microsoft\Test\", "Example");
        var tweak = CatalogEngineTests.Tweak("task") with { ApplyOperations = [op] };
        var plan = new ExecutionPlan(Guid.NewGuid(), DateTimeOffset.UtcNow, [new(tweak, [op])], "00");
        var session = await new ExecutionEngine(executor, new FakeJournal()).ExecuteAsync(plan);
        Assert.Equal("true", session.BeforeStates[0].Metadata?["enabled"]);
    }

    private sealed class FakeExecutor(string? failOn = null, OperationBeforeState? before = null) : IOperationExecutor
    {
        public List<string> RolledBack { get; } = [];
        public Task<OperationBeforeState> CaptureBeforeAsync(TypedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(before ?? new(operation.Id, false, null, null));
        public Task<OperationResult> ExecuteAsync(TypedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(new OperationResult(operation.Id, operation.Id != failOn, operation.Id != failOn, operation.Id == failOn ? 1 : 0, "fake", TimeSpan.Zero));
        public Task<OperationResult> RollbackAsync(TypedOperation operation, OperationBeforeState beforeState, CancellationToken cancellationToken = default) { RolledBack.Add(operation.Id); return Task.FromResult(new OperationResult(operation.Id, true, true, 0, "restored", TimeSpan.Zero)); }
    }

    private sealed class FakeJournal : IJournalStore
    {
        public string RootPath => "memory";
        public bool PlanWrittenBeforeSession { get; private set; }
        private bool _planWritten;
        public Task WritePlanAsync(ExecutionPlan plan, CancellationToken cancellationToken = default) { _planWritten = true; return Task.CompletedTask; }
        public Task WriteSessionAsync(JournalSession session, CancellationToken cancellationToken = default) { PlanWrittenBeforeSession = _planWritten; return Task.CompletedTask; }
        public Task<JournalSession?> ReadSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) => Task.FromResult<JournalSession?>(null);
    }
}
