using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Application.Tests;

public sealed class BackupExecutorTests
{
    [Fact]
    public async Task NotRequiredCompletesWithoutCallingStorage()
    {
        var storage = new StubStorage();
        var executor = new BackupExecutor(storage, new BackupPlanner());
        var plan = new BackupPlan(BackupPlanStatus.NotRequired, [], []);

        BackupExecutionResult result = await executor.ExecuteAsync(
            "destination",
            "backup-parent",
            plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(BackupExecutionStatus.NotRequired, result.Status);
        Assert.True(result.IsComplete);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task BlockedPlanFailsBeforeFilesystemBoundary()
    {
        var storage = new StubStorage();
        var executor = new BackupExecutor(storage, new BackupPlanner());
        var plan = new BackupPlan(
            BackupPlanStatus.Blocked,
            [],
            [new BackupBlocker(BackupBlockerKind.MigrationPlanNotReady)]);

        BackupExecutionResult result = await executor.ExecuteAsync(
            "destination",
            "backup-parent",
            plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(BackupExecutionStatus.Failed, result.Status);
        Assert.Equal(BackupFailureKind.InvalidPlan, result.FailureKind);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task PreCancelledRequestDoesNotCreateBackupRoot()
    {
        var storage = new StubStorage();
        var executor = new BackupExecutor(storage, new BackupPlanner());
        var plan = ReadyPlan();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        BackupExecutionResult result = await executor.ExecuteAsync(
            "destination",
            "backup-parent",
            plan,
            cancellation.Token);

        Assert.Equal(BackupExecutionStatus.Cancelled, result.Status);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task ReadyPlanCreatesManifestAndDelegatesOnce()
    {
        var storage = new StubStorage
        {
            Result = new BackupExecutionResult(
                BackupExecutionStatus.Completed,
                @"C:\backup",
                EntriesCopied: 1,
                Verification: new BackupVerificationSummary(1, 1, 42, "ABC")),
        };
        var executor = new BackupExecutor(storage, new BackupPlanner());

        BackupExecutionResult result = await executor.ExecuteAsync(
            "destination",
            "backup-parent",
            ReadyPlan(),
            TestContext.Current.CancellationToken);

        Assert.Equal(BackupExecutionStatus.Completed, result.Status);
        Assert.Equal(1, storage.Calls);
        Assert.NotNull(storage.Manifest);
        Assert.Single(storage.Manifest.Entries);
        Assert.Equal(1, result.EntriesCopied);
    }

    private static BackupPlan ReadyPlan() =>
        new(
            BackupPlanStatus.Ready,
            [new BackupPlanEntry("config", ExpectedEntryKind.Directory, EntryState.Directory)],
            []);

    private sealed class StubStorage : IBackupStorage
    {
        public int Calls { get; private set; }

        public BackupManifestDraft? Manifest { get; private set; }

        public BackupExecutionResult Result { get; init; } =
            new(BackupExecutionStatus.Completed, "backup");

        public Task<BackupExecutionResult> CreateBackupAsync(
            string destinationRoot,
            string backupParent,
            BackupPlan plan,
            BackupManifestDraft manifest,
            CancellationToken cancellationToken)
        {
            Calls++;
            Manifest = manifest;
            return Task.FromResult(Result);
        }
    }
}
