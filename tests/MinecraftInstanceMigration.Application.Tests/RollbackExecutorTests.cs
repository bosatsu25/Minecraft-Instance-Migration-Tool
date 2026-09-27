using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Application.Tests;

public sealed class RollbackExecutorTests
{
    [Fact]
    public async Task NotRequiredPlanBypassesStorage()
    {
        var backup = new StubBackupValidator();
        var storage = new StubStorage();
        var executor = new RollbackExecutor(backup, storage);

        RollbackExecutionResult result = await executor.ExecuteAsync(
            new RollbackExecutionRequest(
                "destination",
                null,
                null,
                new RollbackPlan(RollbackPlanStatus.NotRequired, [], [])),
            TestContext.Current.CancellationToken);

        Assert.Equal(RollbackExecutionStatus.NotRequired, result.Status);
        Assert.Equal(0, storage.Calls);
        Assert.Equal(0, backup.Calls);
    }

    [Fact]
    public async Task RecoveryRequiredPlanNeverAttemptsAutomaticStorage()
    {
        var backup = new StubBackupValidator();
        var storage = new StubStorage();
        var executor = new RollbackExecutor(backup, storage);
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

        RollbackExecutionResult result = await executor.ExecuteAsync(
            new RollbackExecutionRequest(
                "destination",
                null,
                null,
                plan),
            TestContext.Current.CancellationToken);

        Assert.Equal(RollbackExecutionStatus.RecoveryRequired, result.Status);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task DeleteCreatedEntryDoesNotRequireBackupValidation()
    {
        var backup = new StubBackupValidator();
        var storage = new StubStorage();
        var executor = new RollbackExecutor(backup, storage);

        RollbackExecutionResult result = await executor.ExecuteAsync(
            Request(ReadyPlan(DeleteAction("options.txt"))),
            TestContext.Current.CancellationToken);

        Assert.Equal(RollbackExecutionStatus.Completed, result.Status);
        Assert.Equal(1, result.CompletedActions);
        Assert.Equal(1, storage.Calls);
        Assert.Equal(0, backup.Calls);
    }

    [Fact]
    public async Task RestoreRevalidatesBackupBeforeStorage()
    {
        var events = new List<string>();
        var backup = new StubBackupValidator(events);
        var storage = new StubStorage(events);
        var executor = new RollbackExecutor(backup, storage);

        RollbackExecutionResult result = await executor.ExecuteAsync(
            Request(
                ReadyPlan(RestoreAction("config")),
                backupRoot: "backup",
                backupPlan: ReadyBackupPlan()),
            TestContext.Current.CancellationToken);

        Assert.Equal(RollbackExecutionStatus.Completed, result.Status);
        Assert.Equal(new[] { "backup", "storage:config" }, events);
    }

    [Fact]
    public async Task InvalidBackupBlocksRestoreBeforeStorage()
    {
        var backup = new StubBackupValidator
        {
            Result = new BackupArtifactValidationResult(
                BackupArtifactValidationStatus.Invalid,
                BackupArtifactFailureKind.VerificationMismatch),
        };
        var storage = new StubStorage();
        var executor = new RollbackExecutor(backup, storage);

        RollbackExecutionResult result = await executor.ExecuteAsync(
            Request(
                ReadyPlan(RestoreAction("config")),
                backupRoot: "backup",
                backupPlan: ReadyBackupPlan()),
            TestContext.Current.CancellationToken);

        Assert.Equal(RollbackExecutionStatus.Blocked, result.Status);
        Assert.Equal(RollbackExecutionFailureKind.BackupInvalid, result.FailureKind);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task CancellationDuringSuccessfulBackupValidationStopsBeforeStorage()
    {
        using var cancellation = new CancellationTokenSource();
        var backup = new StubBackupValidator
        {
            OnValidation = cancellation.Cancel,
        };
        var storage = new StubStorage();

        RollbackExecutionResult result = await new RollbackExecutor(
            backup, storage).ExecuteAsync(
                Request(
                    ReadyPlan(RestoreAction("config")),
                    backupRoot: "backup",
                    backupPlan: ReadyBackupPlan()),
                cancellation.Token);

        Assert.Equal(RollbackExecutionStatus.Cancelled, result.Status);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task FirstGuardRejectionBlocksWithoutClaimingRollback()
    {
        var storage = new StubStorage
        {
            ResultForCall = _ => new RollbackStorageResult(
                RollbackStorageStatus.GuardRejected,
                RollbackStorageFailureKind.DestinationChanged),
        };
        var executor = new RollbackExecutor(new StubBackupValidator(), storage);

        RollbackExecutionResult result = await executor.ExecuteAsync(
            Request(ReadyPlan(DeleteAction("options.txt"))),
            TestContext.Current.CancellationToken);

        Assert.Equal(RollbackExecutionStatus.Blocked, result.Status);
        Assert.Equal(RollbackExecutionFailureKind.GuardRejected, result.FailureKind);
        Assert.Equal(0, result.CompletedActions);
    }

    [Fact]
    public async Task LaterGuardRejectionAfterOneRollbackRequiresRecovery()
    {
        var storage = new StubStorage
        {
            ResultForCall = call => call == 1
                ? new RollbackStorageResult(RollbackStorageStatus.Applied)
                : new RollbackStorageResult(
                    RollbackStorageStatus.GuardRejected,
                    RollbackStorageFailureKind.DestinationChanged),
        };
        var executor = new RollbackExecutor(new StubBackupValidator(), storage);

        RollbackExecutionResult result = await executor.ExecuteAsync(
            Request(ReadyPlan(
                DeleteAction("config", 0, ExpectedEntryKind.Directory),
                DeleteAction("options.txt", 1))),
            TestContext.Current.CancellationToken);

        Assert.Equal(RollbackExecutionStatus.RecoveryRequired, result.Status);
        Assert.Equal(RollbackExecutionFailureKind.GuardRejected, result.FailureKind);
        Assert.Equal(1, result.CompletedActions);
    }

    [Fact]
    public async Task CancellationBeforeFirstActionDoesNotCallStorage()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var storage = new StubStorage();
        var executor = new RollbackExecutor(new StubBackupValidator(), storage);

        RollbackExecutionResult result = await executor.ExecuteAsync(
            Request(ReadyPlan(DeleteAction("options.txt"))),
            cancellation.Token);

        Assert.Equal(RollbackExecutionStatus.Cancelled, result.Status);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task CancellationAfterOneAppliedRollbackRequiresRecovery()
    {
        using var cancellation = new CancellationTokenSource();
        var storage = new StubStorage
        {
            OnCall = call =>
            {
                if (call == 1)
                {
                    cancellation.Cancel();
                }
            },
        };
        var executor = new RollbackExecutor(new StubBackupValidator(), storage);

        RollbackExecutionResult result = await executor.ExecuteAsync(
            Request(ReadyPlan(
                DeleteAction("config", 0, ExpectedEntryKind.Directory),
                DeleteAction("options.txt", 1))),
            cancellation.Token);

        Assert.Equal(RollbackExecutionStatus.RecoveryRequired, result.Status);
        Assert.Equal(
            RollbackExecutionFailureKind.CancelledAfterPartialRollback,
            result.FailureKind);
        Assert.Equal(1, result.CompletedActions);
        Assert.Equal(1, storage.Calls);
    }

    [Fact]
    public async Task StorageRecoveryRequiredPropagatesAsRecoveryRequired()
    {
        var storage = new StubStorage
        {
            ResultForCall = _ => new RollbackStorageResult(
                RollbackStorageStatus.RecoveryRequired,
                RollbackStorageFailureKind.VerificationFailed),
        };
        var executor = new RollbackExecutor(new StubBackupValidator(), storage);

        RollbackExecutionResult result = await executor.ExecuteAsync(
            Request(ReadyPlan(DeleteAction("options.txt"))),
            TestContext.Current.CancellationToken);

        Assert.Equal(RollbackExecutionStatus.RecoveryRequired, result.Status);
        Assert.Equal(RollbackExecutionFailureKind.StorageFailure, result.FailureKind);
    }

    private static RollbackExecutionRequest Request(
        RollbackPlan plan,
        string? backupRoot = null,
        BackupPlan? backupPlan = null) =>
        new("destination", backupRoot, backupPlan, plan);

    private static RollbackPlan ReadyPlan(params RollbackPlanEntry[] entries) =>
        new(RollbackPlanStatus.Ready, entries, []);

    private static RollbackPlanEntry DeleteAction(
        string name,
        int order = 0,
        ExpectedEntryKind kind = ExpectedEntryKind.File) =>
        new(
            order,
            name,
            kind,
            ExecutionOperationKind.Copy,
            RollbackActionKind.DeleteCreatedEntry,
            Fingerprint('A'));

    private static RollbackPlanEntry RestoreAction(
        string name,
        int order = 0) =>
        new(
            order,
            name,
            ExpectedEntryKind.Directory,
            ExecutionOperationKind.Replace,
            RollbackActionKind.RestoreFromBackup,
            Fingerprint('B'));

    private static BackupPlan ReadyBackupPlan() =>
        new(
            BackupPlanStatus.Ready,
            [
                new BackupPlanEntry(
                    "config",
                    ExpectedEntryKind.Directory,
                    EntryState.Directory),
            ],
            []);

    private static ExecutionContentFingerprint Fingerprint(char seed) =>
        new(1, 0, 1, new string(seed, 64));

    private sealed class StubBackupValidator : IBackupArtifactValidator
    {
        private readonly List<string>? events;

        public StubBackupValidator(List<string>? events = null)
        {
            this.events = events;
        }

        public int Calls { get; private set; }

        public Action? OnValidation { get; set; }

        public BackupArtifactValidationResult Result { get; set; } =
            new(
                BackupArtifactValidationStatus.Valid,
                Verification: new BackupVerificationSummary(
                    1,
                    1,
                    1,
                    new string('C', 64)));

        public Task<BackupArtifactValidationResult> ValidateAsync(
            string backupRoot,
            BackupPlan plan,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            events?.Add("backup");
            OnValidation?.Invoke();
            return Task.FromResult(Result);
        }
    }

    private sealed class StubStorage : IRollbackStorage
    {
        private readonly List<string>? events;

        public StubStorage(List<string>? events = null)
        {
            this.events = events;
        }

        public int Calls { get; private set; }

        public Func<int, RollbackStorageResult>? ResultForCall { get; set; }

        public Action<int>? OnCall { get; set; }

        public Task<RollbackStorageResult> ApplyAsync(
            string destinationRoot,
            string? backupRoot,
            RollbackBackupEvidence? backupEvidence,
            RollbackPlanEntry action,
            CancellationToken cancellationToken)
        {
            Calls++;
            events?.Add($"storage:{action.Name}");
            OnCall?.Invoke(Calls);

            return Task.FromResult(
                ResultForCall?.Invoke(Calls) ??
                new RollbackStorageResult(RollbackStorageStatus.Applied));
        }
    }
}
