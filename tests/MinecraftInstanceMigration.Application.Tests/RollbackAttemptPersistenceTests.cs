using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Application.Tests;

public sealed class RollbackAttemptPersistenceTests
{
    [Fact]
    public async Task NonAutomaticPlanNeverCrossesStorageBoundary()
    {
        var storage = new StubStorage();
        var persistence = new RollbackAttemptPersistence(storage);
        var plan = new RollbackPlan(
            RollbackPlanStatus.RecoveryRequired,
            [
                new RollbackPlanEntry(
                    0,
                    "config",
                    ExpectedEntryKind.Directory,
                    ExecutionOperationKind.Replace,
                    RollbackActionKind.ManualRecoveryRequired,
                    RecoveryReason: RollbackRecoveryReason.OutcomeUncertain),
            ],
            []);

        RollbackAttemptWriteResult result = await persistence.CreateAsync(
            "journals",
            "destination",
            null,
            plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(RollbackAttemptWriteStatus.Invalid, result.Status);
        Assert.Equal(
            RollbackAttemptPersistenceFailureKind.InvalidPlan,
            result.FailureKind);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task PreCancelledCreateNeverCrossesStorageBoundary()
    {
        var storage = new StubStorage();
        var persistence = new RollbackAttemptPersistence(storage);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        RollbackAttemptWriteResult result = await persistence.CreateAsync(
            "journals",
            "destination",
            null,
            ReadyPlan(),
            cancellation.Token);

        Assert.Equal(RollbackAttemptWriteStatus.Cancelled, result.Status);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task InvalidOrderNeverCrossesStorageBoundary()
    {
        var storage = new StubStorage();
        var persistence = new RollbackAttemptPersistence(storage);

        RollbackAttemptWriteResult result =
            await persistence.MarkActionStartedAsync(
                Reference(),
                ReadyPlan(),
                7,
                TestContext.Current.CancellationToken);

        Assert.Equal(RollbackAttemptWriteStatus.Invalid, result.Status);
        Assert.Equal(
            RollbackAttemptPersistenceFailureKind.JournalStateInvalid,
            result.FailureKind);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task ValidOperationsDelegateToStorage()
    {
        var storage = new StubStorage();
        var persistence = new RollbackAttemptPersistence(storage);
        RollbackPlan plan = ReadyPlan();
        RollbackAttemptReference attempt = Reference();

        await persistence.CreateAsync(
            "journals",
            "destination",
            null,
            plan,
            TestContext.Current.CancellationToken);
        await persistence.MarkActionStartedAsync(
            attempt,
            plan,
            0,
            TestContext.Current.CancellationToken);
        await persistence.MarkActionAppliedAsync(
            attempt,
            plan,
            0,
            TestContext.Current.CancellationToken);
        await persistence.LoadAsync(
            attempt,
            plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(4, storage.Calls);
    }

    private static RollbackPlan ReadyPlan() =>
        new(
            RollbackPlanStatus.Ready,
            [
                new RollbackPlanEntry(
                    0,
                    "options.txt",
                    ExpectedEntryKind.File,
                    ExecutionOperationKind.Copy,
                    RollbackActionKind.DeleteCreatedEntry,
                    new ExecutionContentFingerprint(
                        1,
                        0,
                        4,
                        new string('A', 64))),
            ],
            []);

    private static RollbackAttemptReference Reference() =>
        new(
            "0123456789abcdef0123456789abcdef",
            @"C:\journals\mim-rollback-0123456789abcdef0123456789abcdef.jsonl");

    private sealed class StubStorage : IRollbackAttemptStorage
    {
        public int Calls { get; private set; }

        public Task<RollbackAttemptWriteResult> CreateAsync(
            string journalParent,
            string destinationRoot,
            string? backupRoot,
            RollbackPlan plan,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new RollbackAttemptWriteResult(
                RollbackAttemptWriteStatus.Succeeded,
                Reference()));
        }

        public Task<RollbackAttemptWriteResult> MarkActionStartedAsync(
            RollbackAttemptReference attempt,
            RollbackPlan plan,
            int order,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new RollbackAttemptWriteResult(
                RollbackAttemptWriteStatus.Succeeded,
                attempt));
        }

        public Task<RollbackAttemptWriteResult> MarkActionAppliedAsync(
            RollbackAttemptReference attempt,
            RollbackPlan plan,
            int order,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new RollbackAttemptWriteResult(
                RollbackAttemptWriteStatus.Succeeded,
                attempt));
        }

        public Task<RollbackAttemptWriteResult> MarkActionGuardRejectedAsync(
            RollbackAttemptReference attempt,
            RollbackPlan plan,
            int order,
            RollbackStorageFailureKind failureKind,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new RollbackAttemptWriteResult(
                RollbackAttemptWriteStatus.Succeeded,
                attempt));
        }

        public Task<RollbackAttemptWriteResult> MarkActionFailedAsync(
            RollbackAttemptReference attempt,
            RollbackPlan plan,
            int order,
            RollbackStorageFailureKind failureKind,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new RollbackAttemptWriteResult(
                RollbackAttemptWriteStatus.Succeeded,
                attempt));
        }

        public Task<RollbackAttemptReadResult> LoadAsync(
            RollbackAttemptReference attempt,
            RollbackPlan plan,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new RollbackAttemptReadResult(
                RollbackAttemptReadStatus.Loaded,
                new RollbackAttemptSnapshot(
                    RollbackAttemptSnapshot.CurrentSchemaVersion,
                    [
                        new RollbackAttemptStep(
                            0,
                            "options.txt",
                            RollbackActionKind.DeleteCreatedEntry,
                            RollbackAttemptStepOutcome.Applied),
                    ])));
        }
    }
}
