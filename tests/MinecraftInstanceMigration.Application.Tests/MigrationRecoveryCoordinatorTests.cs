using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Application.Tests;

public sealed class MigrationRecoveryCoordinatorTests
{
    [Fact]
    public async Task AppliedCopyEvidenceProducesRollbackableDeletePlan()
    {
        var persistence = new StubJournalPersistence(Snapshot(ExecutionStepOutcome.Applied));
        var rollback = new StubRollbackExecutor();
        var coordinator = Coordinator(persistence, rollback);

        MigrationRecoveryDiagnosis diagnosis = await coordinator.DiagnoseAsync(
            Request(), TestContext.Current.CancellationToken);

        Assert.Equal(MigrationRecoveryDiagnosisStatus.RollbackAvailable, diagnosis.Status);
        Assert.True(diagnosis.CanRollback);
        Assert.Equal(1, diagnosis.AppliedExecutionSteps);
        Assert.Equal(1, diagnosis.DeleteCreatedEntryCount);
        Assert.Equal(0, diagnosis.RestoreFromBackupCount);
        Assert.False(diagnosis.BackupRequired);
        Assert.Equal(RollbackActionKind.DeleteCreatedEntry, Assert.Single(diagnosis.RollbackPlan!.Entries).Action);
    }

    [Fact]
    public async Task FailedOrUncertainExecutionEvidenceRequiresManualRecovery()
    {
        foreach (ExecutionStepOutcome outcome in new[] { ExecutionStepOutcome.Failed, ExecutionStepOutcome.Uncertain })
        {
            MigrationRecoveryDiagnosis diagnosis = await Coordinator(
                new StubJournalPersistence(Snapshot(outcome)),
                new StubRollbackExecutor()).DiagnoseAsync(Request(), TestContext.Current.CancellationToken);

            Assert.Equal(MigrationRecoveryDiagnosisStatus.ManualRecoveryRequired, diagnosis.Status);
            Assert.False(diagnosis.CanRollback);
            Assert.Equal(outcome == ExecutionStepOutcome.Failed ? 1 : 0, diagnosis.FailedExecutionSteps);
            Assert.Equal(outcome == ExecutionStepOutcome.Uncertain ? 1 : 0, diagnosis.UncertainExecutionSteps);
        }
    }

    [Fact]
    public async Task InvalidJournalFailsClosed()
    {
        var persistence = new StubJournalPersistence(null)
        {
            Result = new ExecutionJournalReadResult(ExecutionJournalReadStatus.Invalid),
        };
        var rollback = new StubRollbackExecutor();

        MigrationRecoveryDiagnosis diagnosis = await Coordinator(persistence, rollback)
            .DiagnoseAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(MigrationRecoveryDiagnosisStatus.Blocked, diagnosis.Status);
        Assert.False(diagnosis.CanRollback);
        Assert.Equal(0, rollback.Calls);
    }

    [Fact]
    public async Task ReplacePlanRequiresValidatedBackup()
    {
        var backup = new StubBackupValidator(new BackupArtifactValidationResult(
            BackupArtifactValidationStatus.Invalid,
            BackupArtifactFailureKind.VerificationMismatch));
        var request = Request(replace: true);

        MigrationRecoveryDiagnosis diagnosis = await Coordinator(
            new StubJournalPersistence(Snapshot(ExecutionStepOutcome.Applied, replace: true)),
            new StubRollbackExecutor(),
            backup).DiagnoseAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(1, backup.Calls);
        Assert.Equal(MigrationRecoveryDiagnosisStatus.ManualRecoveryRequired, diagnosis.Status);
        Assert.False(diagnosis.CanRollback);
        Assert.True(diagnosis.BackupRequired);
    }

    [Fact]
    public async Task ExecuteUsesExactDiagnosedPlanAndLoadsDurableAttemptEvidence()
    {
        var persistence = new StubJournalPersistence(Snapshot(ExecutionStepOutcome.Applied));
        var attempt = new StubAttemptPersistence(new RollbackAttemptSnapshot(
            RollbackAttemptSnapshot.CurrentSchemaVersion,
            [new RollbackAttemptStep(0, "config", RollbackActionKind.DeleteCreatedEntry, RollbackAttemptStepOutcome.Applied)]));
        var rollback = new StubRollbackExecutor
        {
            Result = new RollbackExecutionResult(
                RollbackExecutionStatus.Completed,
                CompletedActions: 1,
                Attempt: Attempt),
        };
        var coordinator = Coordinator(persistence, rollback, attemptPersistence: attempt);
        MigrationRecoveryRequest request = Request();
        MigrationRecoveryDiagnosis diagnosis = await coordinator.DiagnoseAsync(request, TestContext.Current.CancellationToken);

        MigrationRecoveryResult result = await coordinator.ExecuteAsync(
            request, diagnosis, TestContext.Current.CancellationToken);

        Assert.Equal(MigrationRecoveryOutcome.Applied, result.Outcome);
        Assert.Equal(1, result.AppliedActions);
        Assert.Same(diagnosis.RollbackPlan, rollback.Request?.RollbackPlan);
        Assert.Equal(1, rollback.Calls);
        Assert.Equal(1, attempt.Calls);
    }

    [Theory]
    [InlineData(RollbackAttemptStepOutcome.GuardRejected, MigrationRecoveryOutcome.GuardRejected)]
    [InlineData(RollbackAttemptStepOutcome.Failed, MigrationRecoveryOutcome.Failed)]
    [InlineData(RollbackAttemptStepOutcome.Uncertain, MigrationRecoveryOutcome.Uncertain)]
    public async Task DurableAttemptEvidenceControlsPresentedOutcome(
        RollbackAttemptStepOutcome stepOutcome,
        MigrationRecoveryOutcome expected)
    {
        var attempt = new StubAttemptPersistence(new RollbackAttemptSnapshot(
            RollbackAttemptSnapshot.CurrentSchemaVersion,
            [new RollbackAttemptStep(0, "config", RollbackActionKind.DeleteCreatedEntry, stepOutcome)]));
        var rollback = new StubRollbackExecutor
        {
            Result = new RollbackExecutionResult(
                stepOutcome == RollbackAttemptStepOutcome.GuardRejected
                    ? RollbackExecutionStatus.Blocked
                    : RollbackExecutionStatus.RecoveryRequired,
                Attempt: Attempt),
        };
        var coordinator = Coordinator(
            new StubJournalPersistence(Snapshot(ExecutionStepOutcome.Applied)),
            rollback,
            attemptPersistence: attempt);
        MigrationRecoveryRequest request = Request();
        MigrationRecoveryDiagnosis diagnosis = await coordinator.DiagnoseAsync(request, TestContext.Current.CancellationToken);

        MigrationRecoveryResult result = await coordinator.ExecuteAsync(request, diagnosis, TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public async Task NonRollbackableDiagnosisNeverCallsExecutor()
    {
        var rollback = new StubRollbackExecutor();
        var coordinator = Coordinator(
            new StubJournalPersistence(Snapshot(ExecutionStepOutcome.Uncertain)), rollback);
        MigrationRecoveryRequest request = Request();
        MigrationRecoveryDiagnosis diagnosis = await coordinator.DiagnoseAsync(request, TestContext.Current.CancellationToken);

        MigrationRecoveryResult result = await coordinator.ExecuteAsync(request, diagnosis, TestContext.Current.CancellationToken);

        Assert.Equal(MigrationRecoveryOutcome.Blocked, result.Outcome);
        Assert.Equal(0, rollback.Calls);
    }

    [Fact]
    public async Task DiagnosisAuthorizationIsBoundToExactRequestAndSingleUse()
    {
        var rollback = new StubRollbackExecutor();
        var coordinator = Coordinator(
            new StubJournalPersistence(Snapshot(ExecutionStepOutcome.Applied)), rollback);
        MigrationRecoveryRequest request = Request();
        MigrationRecoveryDiagnosis diagnosis = await coordinator.DiagnoseAsync(request, TestContext.Current.CancellationToken);

        MigrationRecoveryResult stale = await coordinator.ExecuteAsync(
            request with { DestinationRoot = "other-destination" },
            diagnosis,
            TestContext.Current.CancellationToken);
        MigrationRecoveryResult replay = await coordinator.ExecuteAsync(
            request,
            diagnosis,
            TestContext.Current.CancellationToken);

        Assert.Equal(MigrationRecoveryOutcome.Blocked, stale.Outcome);
        Assert.Equal(MigrationRecoveryOutcome.Blocked, replay.Outcome);
        Assert.Equal(0, rollback.Calls);

        MigrationRecoveryDiagnosis second = await coordinator.DiagnoseAsync(request, TestContext.Current.CancellationToken);
        MigrationRecoveryDiagnosis forged = second with
        {
            RollbackPlan = new RollbackPlan(RollbackPlanStatus.Ready, second.RollbackPlan!.Entries, []),
        };
        MigrationRecoveryResult changedPlan = await coordinator.ExecuteAsync(
            request,
            forged,
            TestContext.Current.CancellationToken);

        Assert.Equal(MigrationRecoveryOutcome.Blocked, changedPlan.Outcome);
        Assert.Equal(0, rollback.Calls);
    }

    [Fact]
    public async Task AppliedThenGuardRejectedRemainsPartialRecoveryEvidence()
    {
        var request = Request(twoCopies: true);
        var attempt = new StubAttemptPersistence(new RollbackAttemptSnapshot(
            RollbackAttemptSnapshot.CurrentSchemaVersion,
            [
                new RollbackAttemptStep(0, "options.txt", RollbackActionKind.DeleteCreatedEntry, RollbackAttemptStepOutcome.Applied),
                new RollbackAttemptStep(1, "config", RollbackActionKind.DeleteCreatedEntry, RollbackAttemptStepOutcome.GuardRejected),
            ]));
        var rollback = new StubRollbackExecutor
        {
            Result = new RollbackExecutionResult(
                RollbackExecutionStatus.RecoveryRequired,
                CompletedActions: 1,
                Attempt: Attempt),
        };
        var coordinator = Coordinator(
            new StubJournalPersistence(SnapshotTwoAppliedCopies()),
            rollback,
            attemptPersistence: attempt);
        MigrationRecoveryDiagnosis diagnosis = await coordinator.DiagnoseAsync(request, TestContext.Current.CancellationToken);

        MigrationRecoveryResult result = await coordinator.ExecuteAsync(request, diagnosis, TestContext.Current.CancellationToken);

        Assert.Equal(MigrationRecoveryOutcome.GuardRejected, result.Outcome);
        Assert.Equal(1, result.AppliedActions);
        Assert.Equal(1, result.GuardRejectedActions);
        Assert.False(result.IsRecovered);
    }

    private static MigrationRecoveryCoordinator Coordinator(
        IExecutionJournalPersistence journal,
        IRollbackExecutor rollback,
        IBackupArtifactValidator? backup = null,
        IRollbackAttemptPersistence? attemptPersistence = null) =>
        new(
            new ExecutionSafetyPlanner(),
            journal,
            backup ?? new StubBackupValidator(ValidBackup),
            rollback,
            attemptPersistence ?? new StubAttemptPersistence(null));

    private static MigrationRecoveryRequest Request(bool replace = false, bool twoCopies = false)
    {
        ExecutionOperationKind operation = replace ? ExecutionOperationKind.Replace : ExecutionOperationKind.Copy;
        var plan = new MinecraftInstanceMigration.Domain.Planning.MigrationPlan(
            EntryState.Directory,
            EntryState.Directory,
            twoCopies
                ? [
                    new MinecraftInstanceMigration.Domain.Planning.MigrationPlanEntry(
                        "config", ExpectedEntryKind.Directory, EntryState.Directory, EntryState.Missing, true,
                        MinecraftInstanceMigration.Domain.Planning.MigrationPlanDisposition.ReadyToCopy),
                    new MinecraftInstanceMigration.Domain.Planning.MigrationPlanEntry(
                        "options.txt", ExpectedEntryKind.File, EntryState.File, EntryState.Missing, true,
                        MinecraftInstanceMigration.Domain.Planning.MigrationPlanDisposition.ReadyToCopy),
                ]
                : [new MinecraftInstanceMigration.Domain.Planning.MigrationPlanEntry(
                    "config", ExpectedEntryKind.Directory, EntryState.Directory,
                    replace ? EntryState.Directory : EntryState.Missing, true,
                    replace
                        ? MinecraftInstanceMigration.Domain.Planning.MigrationPlanDisposition.ReadyToReplace
                        : MinecraftInstanceMigration.Domain.Planning.MigrationPlanDisposition.ReadyToCopy)],
            []);
        BackupPlan backupPlan = replace
            ? new BackupPlan(BackupPlanStatus.Ready,
                [new BackupPlanEntry("config", ExpectedEntryKind.Directory, EntryState.Directory)], [])
            : new BackupPlan(BackupPlanStatus.NotRequired, [], []);
        return new MigrationRecoveryRequest(
            "destination", replace ? "backup" : null, backupPlan, "journals", plan, Journal);
    }

    private static ExecutionJournalSnapshot Snapshot(ExecutionStepOutcome outcome, bool replace = false) =>
        new(
            ExecutionJournalDraft.CurrentSchemaVersion,
            [new ExecutionJournalStep(
                0, "config", replace ? ExecutionOperationKind.Replace : ExecutionOperationKind.Copy,
                outcome,
                outcome == ExecutionStepOutcome.Applied ? Fingerprint : null)]);

    private static ExecutionJournalSnapshot SnapshotTwoAppliedCopies() =>
        new(
            ExecutionJournalDraft.CurrentSchemaVersion,
            [
                new ExecutionJournalStep(0, "config", ExecutionOperationKind.Copy, ExecutionStepOutcome.Applied, Fingerprint),
                new ExecutionJournalStep(1, "options.txt", ExecutionOperationKind.Copy, ExecutionStepOutcome.Applied, Fingerprint),
            ]);

    private static readonly ExecutionContentFingerprint Fingerprint = new(1, 0, 1, new string('A', 64));
    private static readonly ExecutionJournalReference Journal = new("journal", "journal-file");
    private static readonly RollbackAttemptReference Attempt = new("attempt", "attempt-file");
    private static readonly BackupArtifactValidationResult ValidBackup = new(
        BackupArtifactValidationStatus.Valid,
        Verification: new BackupVerificationSummary(1, 1, 1, new string('B', 64)));

    private sealed class StubJournalPersistence(ExecutionJournalSnapshot? snapshot) : IExecutionJournalPersistence
    {
        public ExecutionJournalReadResult Result { get; set; } = snapshot is null
            ? new ExecutionJournalReadResult(ExecutionJournalReadStatus.Failed)
            : new ExecutionJournalReadResult(ExecutionJournalReadStatus.Loaded, snapshot);
        public Task<ExecutionJournalReadResult> LoadAsync(ExecutionJournalReference journal, ExecutionJournalDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(Result);
        public Task<ExecutionJournalWriteResult> CreateAsync(string journalParent, ExecutionJournalDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExecutionJournalWriteResult> MarkStepStartedAsync(ExecutionJournalReference journal, ExecutionJournalDraft draft, int sequence, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExecutionJournalWriteResult> MarkStepAppliedAsync(ExecutionJournalReference journal, ExecutionJournalDraft draft, int sequence, ExecutionContentFingerprint fingerprint, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExecutionJournalWriteResult> MarkStepFailedAsync(ExecutionJournalReference journal, ExecutionJournalDraft draft, int sequence, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubBackupValidator(BackupArtifactValidationResult result) : IBackupArtifactValidator
    {
        public int Calls { get; private set; }
        public Task<BackupArtifactValidationResult> ValidateAsync(string backupRoot, BackupPlan plan, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class StubRollbackExecutor : IRollbackExecutor
    {
        public int Calls { get; private set; }
        public RollbackExecutionRequest? Request { get; private set; }
        public RollbackExecutionResult Result { get; set; } = new(RollbackExecutionStatus.Completed);
        public Task<RollbackExecutionResult> ExecuteAsync(RollbackExecutionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            Request = request;
            return Task.FromResult(Result);
        }
    }

    private sealed class StubAttemptPersistence(RollbackAttemptSnapshot? snapshot) : IRollbackAttemptPersistence
    {
        public int Calls { get; private set; }
        public Task<RollbackAttemptReadResult> LoadAsync(RollbackAttemptReference attempt, RollbackPlan plan, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(snapshot is null
                ? new RollbackAttemptReadResult(RollbackAttemptReadStatus.Failed)
                : new RollbackAttemptReadResult(RollbackAttemptReadStatus.Loaded, snapshot));
        }
        public Task<RollbackAttemptWriteResult> CreateAsync(string journalParent, string destinationRoot, string? backupRoot, RollbackPlan plan, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RollbackAttemptWriteResult> MarkActionStartedAsync(RollbackAttemptReference attempt, RollbackPlan plan, int order, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RollbackAttemptWriteResult> MarkActionAppliedAsync(RollbackAttemptReference attempt, RollbackPlan plan, int order, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RollbackAttemptWriteResult> MarkActionGuardRejectedAsync(RollbackAttemptReference attempt, RollbackPlan plan, int order, RollbackStorageFailureKind failureKind, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RollbackAttemptWriteResult> MarkActionFailedAsync(RollbackAttemptReference attempt, RollbackPlan plan, int order, RollbackStorageFailureKind failureKind, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
