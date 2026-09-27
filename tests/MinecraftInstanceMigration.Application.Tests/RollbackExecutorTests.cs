using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Application.Tests;

public sealed class RollbackExecutorTests
{
    [Fact]
    public async Task NotRequiredPlanBypassesAttemptAndStorage()
    {
        var backup = new StubBackupValidator();
        var attempt = new StubAttemptPersistence();
        var storage = new StubStorage();
        var executor = new RollbackExecutor(backup, attempt, storage);

        RollbackExecutionResult result = await executor.ExecuteAsync(
            new RollbackExecutionRequest(
                "destination",
                null,
                null,
                "journals",
                new RollbackPlan(RollbackPlanStatus.NotRequired, [], [])),
            TestContext.Current.CancellationToken);

        Assert.Equal(RollbackExecutionStatus.NotRequired, result.Status);
        Assert.Equal(0, attempt.Calls);
        Assert.Equal(0, storage.Calls);
        Assert.Equal(0, backup.Calls);
    }

    [Fact]
    public async Task RecoveryRequiredPlanNeverCreatesAttempt()
    {
        var attempt = new StubAttemptPersistence();
        var storage = new StubStorage();
        var executor = new RollbackExecutor(
            new StubBackupValidator(),
            attempt,
            storage);
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
                "journals",
                plan),
            TestContext.Current.CancellationToken);

        Assert.Equal(RollbackExecutionStatus.RecoveryRequired, result.Status);
        Assert.Equal(0, attempt.Calls);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task DeleteActionIsJournaledAroundStorage()
    {
        var events = new List<string>();
        var attempt = new StubAttemptPersistence(events);
        var storage = new StubStorage(events);
        var executor = new RollbackExecutor(
            new StubBackupValidator(events),
            attempt,
            storage);

        RollbackExecutionResult result = await executor.ExecuteAsync(
            Request(ReadyPlan(DeleteAction("options.txt"))),
            TestContext.Current.CancellationToken);

        Assert.Equal(RollbackExecutionStatus.Completed, result.Status);
        Assert.Equal(1, result.CompletedActions);
        Assert.NotNull(result.Attempt);
        Assert.Equal(
            new[]
            {
                "attempt:create",
                "attempt:started:0",
                "storage:options.txt",
                "attempt:applied:0",
            },
            events);
    }

    [Fact]
    public async Task RestoreRevalidatesBackupBeforeDurableStarted()
    {
        var events = new List<string>();
        var attempt = new StubAttemptPersistence(events);
        var executor = new RollbackExecutor(
            new StubBackupValidator(events),
            attempt,
            new StubStorage(events));

        RollbackExecutionResult result = await executor.ExecuteAsync(
            Request(
                ReadyPlan(RestoreAction("config")),
                backupRoot: "backup",
                backupPlan: ReadyBackupPlan()),
            TestContext.Current.CancellationToken);

        Assert.Equal(RollbackExecutionStatus.Completed, result.Status);
        Assert.Equal(
            new[]
            {
                "attempt:create",
                "backup",
                "attempt:started:0",
                "storage:config",
                "attempt:applied:0",
            },
            events);
    }

    [Fact]
    public async Task InvalidBackupLeavesActionNotStarted()
    {
        var events = new List<string>();
        var backup = new StubBackupValidator(events)
        {
            Result = new BackupArtifactValidationResult(
                BackupArtifactValidationStatus.Invalid,
                BackupArtifactFailureKind.VerificationMismatch),
        };
        var attempt = new StubAttemptPersistence(events);
        var storage = new StubStorage(events);
        var executor = new RollbackExecutor(backup, attempt, storage);

        RollbackExecutionResult result = await executor.ExecuteAsync(
            Request(
                ReadyPlan(RestoreAction("config")),
                backupRoot: "backup",
                backupPlan: ReadyBackupPlan()),
            TestContext.Current.CancellationToken);

        Assert.Equal(RollbackExecutionStatus.Blocked, result.Status);
        Assert.Equal(RollbackExecutionFailureKind.BackupInvalid, result.FailureKind);
        Assert.Equal(
            new[] { "attempt:create", "backup" },
            events);
        Assert.Equal(0, storage.Calls);
        Assert.NotNull(result.Attempt);
    }

    [Fact]
    public async Task StartedMustBeDurableBeforeRollbackStorage()
    {
        var events = new List<string>();
        var attempt = new StubAttemptPersistence(events)
        {
            StartedResult = new RollbackAttemptWriteResult(
                RollbackAttemptWriteStatus.Failed,
                AttemptReference,
                RollbackAttemptPersistenceFailureKind.IoFailure),
        };
        var storage = new StubStorage(events);
        var executor = new RollbackExecutor(
            new StubBackupValidator(events),
            attempt,
            storage);

        RollbackExecutionResult result = await executor.ExecuteAsync(
            Request(ReadyPlan(DeleteAction("options.txt"))),
            TestContext.Current.CancellationToken);

        Assert.Equal(RollbackExecutionStatus.Blocked, result.Status);
        Assert.Equal(RollbackExecutionFailureKind.JournalFailure, result.FailureKind);
        Assert.Equal(0, storage.Calls);
        Assert.Equal(
            new[] { "attempt:create", "attempt:started:0" },
            events);
    }

    [Fact]
    public async Task GuardRejectionIsPersistedDurably()
    {
        var events = new List<string>();
        var storage = new StubStorage(events)
        {
            ResultForCall = _ => new RollbackStorageResult(
                RollbackStorageStatus.GuardRejected,
                RollbackStorageFailureKind.DestinationChanged),
        };
        var attempt = new StubAttemptPersistence(events);
        var executor = new RollbackExecutor(
            new StubBackupValidator(events),
            attempt,
            storage);

        RollbackExecutionResult result = await executor.ExecuteAsync(
            Request(ReadyPlan(DeleteAction("options.txt"))),
            TestContext.Current.CancellationToken);

        Assert.Equal(RollbackExecutionStatus.Blocked, result.Status);
        Assert.Equal(RollbackExecutionFailureKind.GuardRejected, result.FailureKind);
        Assert.Contains("attempt:rejected:0:DestinationChanged", events);
        Assert.Equal(0, result.CompletedActions);
    }

    [Fact]
    public async Task StorageRecoveryRequiredPersistsFailed()
    {
        var events = new List<string>();
        var storage = new StubStorage(events)
        {
            ResultForCall = _ => new RollbackStorageResult(
                RollbackStorageStatus.RecoveryRequired,
                RollbackStorageFailureKind.VerificationFailed),
        };
        var executor = new RollbackExecutor(
            new StubBackupValidator(events),
            new StubAttemptPersistence(events),
            storage);

        RollbackExecutionResult result = await executor.ExecuteAsync(
            Request(ReadyPlan(DeleteAction("options.txt"))),
            TestContext.Current.CancellationToken);

        Assert.Equal(RollbackExecutionStatus.RecoveryRequired, result.Status);
        Assert.Equal(RollbackExecutionFailureKind.StorageFailure, result.FailureKind);
        Assert.Contains("attempt:failed:0:VerificationFailed", events);
    }

    [Fact]
    public async Task AppliedJournalFailureAfterMutationRequiresRecovery()
    {
        var events = new List<string>();
        var attempt = new StubAttemptPersistence(events)
        {
            AppliedResult = new RollbackAttemptWriteResult(
                RollbackAttemptWriteStatus.Failed,
                AttemptReference,
                RollbackAttemptPersistenceFailureKind.IoFailure),
        };
        var executor = new RollbackExecutor(
            new StubBackupValidator(events),
            attempt,
            new StubStorage(events));

        RollbackExecutionResult result = await executor.ExecuteAsync(
            Request(ReadyPlan(DeleteAction("options.txt"))),
            TestContext.Current.CancellationToken);

        Assert.Equal(RollbackExecutionStatus.RecoveryRequired, result.Status);
        Assert.Equal(RollbackExecutionFailureKind.JournalFailure, result.FailureKind);
        Assert.Equal(0, result.CompletedActions);
        Assert.Contains("storage:options.txt", events);
        Assert.Contains("attempt:applied:0", events);
    }

    [Fact]
    public async Task CancellationBeforeFirstActionDoesNotCreateAttempt()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var attempt = new StubAttemptPersistence();
        var storage = new StubStorage();
        var executor = new RollbackExecutor(
            new StubBackupValidator(),
            attempt,
            storage);

        RollbackExecutionResult result = await executor.ExecuteAsync(
            Request(ReadyPlan(DeleteAction("options.txt"))),
            cancellation.Token);

        Assert.Equal(RollbackExecutionStatus.Cancelled, result.Status);
        Assert.Equal(0, attempt.Calls);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task CancellationAfterOneDurablyAppliedActionRequiresRecovery()
    {
        using var cancellation = new CancellationTokenSource();
        var attempt = new StubAttemptPersistence
        {
            OnApplied = order =>
            {
                if (order == 0)
                {
                    cancellation.Cancel();
                }
            },
        };
        var storage = new StubStorage();
        var executor = new RollbackExecutor(
            new StubBackupValidator(),
            attempt,
            storage);

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
        Assert.NotNull(result.Attempt);
    }

    [Fact]
    public async Task MissingJournalParentBlocksAutomaticRollback()
    {
        var executor = new RollbackExecutor(
            new StubBackupValidator(),
            new StubAttemptPersistence(),
            new StubStorage());

        RollbackExecutionResult result = await executor.ExecuteAsync(
            new RollbackExecutionRequest(
                "destination",
                null,
                null,
                "",
                ReadyPlan(DeleteAction("options.txt"))),
            TestContext.Current.CancellationToken);

        Assert.Equal(RollbackExecutionStatus.Blocked, result.Status);
        Assert.Equal(RollbackExecutionFailureKind.JournalRequired, result.FailureKind);
    }

    private static RollbackExecutionRequest Request(
        RollbackPlan plan,
        string? backupRoot = null,
        BackupPlan? backupPlan = null) =>
        new(
            "destination",
            backupRoot,
            backupPlan,
            "journals",
            plan);

    private static RollbackPlan ReadyPlan(
        params RollbackPlanEntry[] entries) =>
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

    private static readonly RollbackAttemptReference AttemptReference =
        new(
            "0123456789abcdef0123456789abcdef",
            @"C:\journals\mim-rollback-0123456789abcdef0123456789abcdef.jsonl");

    private sealed class StubBackupValidator : IBackupArtifactValidator
    {
        private readonly List<string>? events;

        public StubBackupValidator(List<string>? events = null)
        {
            this.events = events;
        }

        public int Calls { get; private set; }

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
            return Task.FromResult(Result);
        }
    }

    private sealed class StubAttemptPersistence : IRollbackAttemptPersistence
    {
        private readonly List<string>? events;

        public StubAttemptPersistence(List<string>? events = null)
        {
            this.events = events;
        }

        public int Calls { get; private set; }

        public RollbackAttemptWriteResult StartedResult { get; set; } =
            new(
                RollbackAttemptWriteStatus.Succeeded,
                AttemptReference);

        public RollbackAttemptWriteResult AppliedResult { get; set; } =
            new(
                RollbackAttemptWriteStatus.Succeeded,
                AttemptReference);

        public Action<int>? OnApplied { get; set; }

        public Task<RollbackAttemptWriteResult> CreateAsync(
            string journalParent,
            string destinationRoot,
            string? backupRoot,
            RollbackPlan plan,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            events?.Add("attempt:create");
            return Task.FromResult(new RollbackAttemptWriteResult(
                RollbackAttemptWriteStatus.Succeeded,
                AttemptReference));
        }

        public Task<RollbackAttemptWriteResult> MarkActionStartedAsync(
            RollbackAttemptReference attempt,
            RollbackPlan plan,
            int order,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            events?.Add($"attempt:started:{order}");
            return Task.FromResult(StartedResult);
        }

        public Task<RollbackAttemptWriteResult> MarkActionAppliedAsync(
            RollbackAttemptReference attempt,
            RollbackPlan plan,
            int order,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            events?.Add($"attempt:applied:{order}");
            OnApplied?.Invoke(order);
            return Task.FromResult(AppliedResult);
        }

        public Task<RollbackAttemptWriteResult> MarkActionGuardRejectedAsync(
            RollbackAttemptReference attempt,
            RollbackPlan plan,
            int order,
            RollbackStorageFailureKind failureKind,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            events?.Add($"attempt:rejected:{order}:{failureKind}");
            return Task.FromResult(new RollbackAttemptWriteResult(
                RollbackAttemptWriteStatus.Succeeded,
                AttemptReference));
        }

        public Task<RollbackAttemptWriteResult> MarkActionFailedAsync(
            RollbackAttemptReference attempt,
            RollbackPlan plan,
            int order,
            RollbackStorageFailureKind failureKind,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            events?.Add($"attempt:failed:{order}:{failureKind}");
            return Task.FromResult(new RollbackAttemptWriteResult(
                RollbackAttemptWriteStatus.Succeeded,
                AttemptReference));
        }

        public Task<RollbackAttemptReadResult> LoadAsync(
            RollbackAttemptReference attempt,
            RollbackPlan plan,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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

        public Task<RollbackStorageResult> ApplyAsync(
            string destinationRoot,
            string? backupRoot,
            RollbackBackupEvidence? backupEvidence,
            RollbackPlanEntry action,
            CancellationToken cancellationToken)
        {
            Calls++;
            events?.Add($"storage:{action.Name}");

            return Task.FromResult(
                ResultForCall?.Invoke(Calls) ??
                new RollbackStorageResult(
                    RollbackStorageStatus.Applied));
        }
    }
}
