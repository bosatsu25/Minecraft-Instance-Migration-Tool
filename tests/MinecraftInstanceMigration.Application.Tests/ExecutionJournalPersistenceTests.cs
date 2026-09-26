using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Application.Tests;

public sealed class ExecutionJournalPersistenceTests
{
    [Fact]
    public async Task BlockedDraftNeverCrossesStorageBoundary()
    {
        var storage = new StubStorage();
        var persistence = new ExecutionJournalPersistence(storage);
        var draft = new ExecutionJournalDraft(
            ExecutionJournalStatus.Blocked,
            [],
            [new ExecutionJournalBlocker(ExecutionJournalBlockerKind.MigrationPlanNotReady)]);

        ExecutionJournalWriteResult result = await persistence.CreateAsync(
            "journal-parent",
            draft,
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionJournalWriteStatus.Invalid, result.Status);
        Assert.Equal(ExecutionJournalPersistenceFailureKind.InvalidDraft, result.FailureKind);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task PreCancelledCreateNeverCrossesStorageBoundary()
    {
        var storage = new StubStorage();
        var persistence = new ExecutionJournalPersistence(storage);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        ExecutionJournalWriteResult result = await persistence.CreateAsync(
            "journal-parent",
            ReadyDraft(),
            cancellation.Token);

        Assert.Equal(ExecutionJournalWriteStatus.Cancelled, result.Status);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task InvalidSequenceNeverCrossesStorageBoundary()
    {
        var storage = new StubStorage();
        var persistence = new ExecutionJournalPersistence(storage);
        ExecutionJournalReference journal = Reference();

        ExecutionJournalWriteResult result = await persistence.MarkStepStartedAsync(
            journal,
            ReadyDraft(),
            7,
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionJournalWriteStatus.Invalid, result.Status);
        Assert.Equal(ExecutionJournalPersistenceFailureKind.JournalStateInvalid, result.FailureKind);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task ReadyOperationsDelegateToStorage()
    {
        var storage = new StubStorage();
        var persistence = new ExecutionJournalPersistence(storage);
        ExecutionJournalDraft draft = ReadyDraft();
        ExecutionJournalReference journal = Reference();
        ExecutionContentFingerprint fingerprint =
            new(1, 0, 4, new string('A', 64));

        await persistence.CreateAsync(
            "journal-parent",
            draft,
            TestContext.Current.CancellationToken);
        await persistence.MarkStepStartedAsync(
            journal,
            draft,
            0,
            TestContext.Current.CancellationToken);
        await persistence.MarkStepAppliedAsync(
            journal,
            draft,
            0,
            fingerprint,
            TestContext.Current.CancellationToken);
        await persistence.LoadAsync(
            journal,
            draft,
            TestContext.Current.CancellationToken);

        Assert.Equal(4, storage.Calls);
    }

    private static ExecutionJournalDraft ReadyDraft() =>
        new(
            ExecutionJournalStatus.Ready,
            [new ExecutionJournalEntry(0, "config", ExpectedEntryKind.Directory, ExecutionOperationKind.Replace)],
            []);

    private static ExecutionJournalReference Reference() =>
        new(
            "0123456789abcdef0123456789abcdef",
            @"C:\journal\mim-journal-0123456789abcdef0123456789abcdef.jsonl");

    private sealed class StubStorage : IExecutionJournalStorage
    {
        public int Calls { get; private set; }

        public Task<ExecutionJournalWriteResult> CreateAsync(
            string journalParent,
            ExecutionJournalDraft draft,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ExecutionJournalWriteResult(
                ExecutionJournalWriteStatus.Succeeded,
                Reference()));
        }

        public Task<ExecutionJournalWriteResult> MarkStepStartedAsync(
            ExecutionJournalReference journal,
            ExecutionJournalDraft draft,
            int sequence,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ExecutionJournalWriteResult(
                ExecutionJournalWriteStatus.Succeeded,
                journal));
        }

        public Task<ExecutionJournalWriteResult> MarkStepAppliedAsync(
            ExecutionJournalReference journal,
            ExecutionJournalDraft draft,
            int sequence,
            ExecutionContentFingerprint fingerprint,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ExecutionJournalWriteResult(
                ExecutionJournalWriteStatus.Succeeded,
                journal));
        }

        public Task<ExecutionJournalWriteResult> MarkStepFailedAsync(
            ExecutionJournalReference journal,
            ExecutionJournalDraft draft,
            int sequence,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ExecutionJournalWriteResult(
                ExecutionJournalWriteStatus.Succeeded,
                journal));
        }

        public Task<ExecutionJournalReadResult> LoadAsync(
            ExecutionJournalReference journal,
            ExecutionJournalDraft draft,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ExecutionJournalReadResult(
                ExecutionJournalReadStatus.Loaded,
                new ExecutionJournalSnapshot(
                    ExecutionJournalDraft.CurrentSchemaVersion,
                    [new ExecutionJournalStep(
                        0,
                        "config",
                        ExecutionOperationKind.Replace,
                        ExecutionStepOutcome.NotStarted)])));
        }
    }
}
