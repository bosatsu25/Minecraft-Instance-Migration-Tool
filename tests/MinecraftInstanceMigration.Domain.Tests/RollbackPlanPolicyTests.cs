using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Domain.Tests;

public sealed class RollbackPlanPolicyTests
{
    [Fact]
    public void NotStartedJournalNeedsNoRollback()
    {
        ExecutionJournalDraft draft = Draft(
            new ExecutionJournalEntry(0, "config", ExpectedEntryKind.Directory, ExecutionOperationKind.Replace));
        var snapshot = new ExecutionJournalSnapshot(
            ExecutionJournalDraft.CurrentSchemaVersion,
            [new ExecutionJournalStep(0, "config", ExecutionOperationKind.Replace, ExecutionStepOutcome.NotStarted)]);

        RollbackPlan rollback = RollbackPlanPolicy.Create(draft, snapshot, validatedBackupAvailable: true);

        Assert.Equal(RollbackPlanStatus.NotRequired, rollback.Status);
        Assert.Empty(rollback.Entries);
    }

    [Fact]
    public void AppliedCopyBecomesDeleteWithFingerprintGuard()
    {
        ExecutionContentFingerprint fingerprint = Fingerprint("A");
        ExecutionJournalDraft draft = Draft(
            new ExecutionJournalEntry(0, "options.txt", ExpectedEntryKind.File, ExecutionOperationKind.Copy));
        var snapshot = new ExecutionJournalSnapshot(
            ExecutionJournalDraft.CurrentSchemaVersion,
            [new ExecutionJournalStep(0, "options.txt", ExecutionOperationKind.Copy, ExecutionStepOutcome.Applied, fingerprint)]);

        RollbackPlan rollback = RollbackPlanPolicy.Create(draft, snapshot, validatedBackupAvailable: false);

        Assert.Equal(RollbackPlanStatus.Ready, rollback.Status);
        Assert.True(rollback.CanAttemptAutomaticRollback);
        RollbackPlanEntry action = Assert.Single(rollback.Entries);
        Assert.Equal(RollbackActionKind.DeleteCreatedEntry, action.Action);
        Assert.Equal(ExpectedEntryKind.File, action.ExpectedKind);
        Assert.Equal(fingerprint, action.ExpectedCurrentFingerprint);
    }

    [Fact]
    public void AppliedReplaceBecomesRestoreOnlyWithValidatedBackup()
    {
        ExecutionContentFingerprint fingerprint = Fingerprint("B");
        ExecutionJournalDraft draft = Draft(
            new ExecutionJournalEntry(0, "config", ExpectedEntryKind.Directory, ExecutionOperationKind.Replace));
        var snapshot = new ExecutionJournalSnapshot(
            ExecutionJournalDraft.CurrentSchemaVersion,
            [new ExecutionJournalStep(0, "config", ExecutionOperationKind.Replace, ExecutionStepOutcome.Applied, fingerprint)]);

        RollbackPlan rollback = RollbackPlanPolicy.Create(draft, snapshot, validatedBackupAvailable: true);

        Assert.Equal(RollbackPlanStatus.Ready, rollback.Status);
        RollbackPlanEntry action = Assert.Single(rollback.Entries);
        Assert.Equal(RollbackActionKind.RestoreFromBackup, action.Action);
        Assert.Equal(ExpectedEntryKind.Directory, action.ExpectedKind);
        Assert.Equal(fingerprint, action.ExpectedCurrentFingerprint);
    }

    [Fact]
    public void AppliedReplaceWithoutValidatedBackupRequiresRecovery()
    {
        ExecutionJournalDraft draft = Draft(
            new ExecutionJournalEntry(0, "config", ExpectedEntryKind.Directory, ExecutionOperationKind.Replace));
        var snapshot = new ExecutionJournalSnapshot(
            ExecutionJournalDraft.CurrentSchemaVersion,
            [
                new ExecutionJournalStep(
                    0,
                    "config",
                    ExecutionOperationKind.Replace,
                    ExecutionStepOutcome.Applied,
                    Fingerprint("C")),
            ]);

        RollbackPlan rollback = RollbackPlanPolicy.Create(draft, snapshot, validatedBackupAvailable: false);

        Assert.Equal(RollbackPlanStatus.RecoveryRequired, rollback.Status);
        Assert.False(rollback.CanAttemptAutomaticRollback);
        RollbackPlanEntry action = Assert.Single(rollback.Entries);
        Assert.Equal(RollbackActionKind.ManualRecoveryRequired, action.Action);
        Assert.Equal(RollbackRecoveryReason.ValidatedBackupUnavailable, action.RecoveryReason);
    }

    [Theory]
    [InlineData(ExecutionStepOutcome.Failed, RollbackRecoveryReason.ExecutionFailed)]
    [InlineData(ExecutionStepOutcome.Uncertain, RollbackRecoveryReason.OutcomeUncertain)]
    public void FailedOrUncertainStepRequiresManualRecovery(
        ExecutionStepOutcome outcome,
        RollbackRecoveryReason reason)
    {
        ExecutionJournalDraft draft = Draft(
            new ExecutionJournalEntry(0, "config", ExpectedEntryKind.Directory, ExecutionOperationKind.Replace));
        var snapshot = new ExecutionJournalSnapshot(
            ExecutionJournalDraft.CurrentSchemaVersion,
            [new ExecutionJournalStep(0, "config", ExecutionOperationKind.Replace, outcome)]);

        RollbackPlan rollback = RollbackPlanPolicy.Create(draft, snapshot, validatedBackupAvailable: true);

        Assert.Equal(RollbackPlanStatus.RecoveryRequired, rollback.Status);
        RollbackPlanEntry action = Assert.Single(rollback.Entries);
        Assert.Equal(ExpectedEntryKind.Directory, action.ExpectedKind);
        Assert.Equal(reason, action.RecoveryReason);
    }

    [Fact]
    public void AppliedStepWithoutPostWriteFingerprintRequiresManualRecovery()
    {
        ExecutionJournalDraft draft = Draft(
            new ExecutionJournalEntry(0, "options.txt", ExpectedEntryKind.File, ExecutionOperationKind.Copy));
        var snapshot = new ExecutionJournalSnapshot(
            ExecutionJournalDraft.CurrentSchemaVersion,
            [new ExecutionJournalStep(0, "options.txt", ExecutionOperationKind.Copy, ExecutionStepOutcome.Applied)]);

        RollbackPlan rollback = RollbackPlanPolicy.Create(draft, snapshot, validatedBackupAvailable: false);

        Assert.Equal(RollbackPlanStatus.RecoveryRequired, rollback.Status);
        Assert.Equal(
            RollbackRecoveryReason.MissingPostWriteFingerprint,
            Assert.Single(rollback.Entries).RecoveryReason);
    }

    [Fact]
    public void RollbackOrderIsReverseExecutionOrder()
    {
        ExecutionJournalDraft draft = Draft(
            new ExecutionJournalEntry(0, "options.txt", ExpectedEntryKind.File, ExecutionOperationKind.Copy),
            new ExecutionJournalEntry(1, "config", ExpectedEntryKind.Directory, ExecutionOperationKind.Replace));
        var snapshot = new ExecutionJournalSnapshot(
            ExecutionJournalDraft.CurrentSchemaVersion,
            [
                new ExecutionJournalStep(0, "options.txt", ExecutionOperationKind.Copy, ExecutionStepOutcome.Applied, Fingerprint("D")),
                new ExecutionJournalStep(1, "config", ExecutionOperationKind.Replace, ExecutionStepOutcome.Applied, Fingerprint("E")),
            ]);

        RollbackPlan rollback = RollbackPlanPolicy.Create(draft, snapshot, validatedBackupAvailable: true);

        Assert.Equal(RollbackPlanStatus.Ready, rollback.Status);
        Assert.Collection(
            rollback.Entries,
            entry =>
            {
                Assert.Equal(0, entry.Order);
                Assert.Equal("config", entry.Name);
                Assert.Equal(RollbackActionKind.RestoreFromBackup, entry.Action);
            },
            entry =>
            {
                Assert.Equal(1, entry.Order);
                Assert.Equal("options.txt", entry.Name);
                Assert.Equal(RollbackActionKind.DeleteCreatedEntry, entry.Action);
            });
    }

    [Fact]
    public void JournalSchemaMismatchBlocksRollback()
    {
        ExecutionJournalDraft draft = Draft(
            new ExecutionJournalEntry(0, "config", ExpectedEntryKind.Directory, ExecutionOperationKind.Replace));
        var snapshot = new ExecutionJournalSnapshot(
            ExecutionJournalDraft.CurrentSchemaVersion + 1,
            [new ExecutionJournalStep(0, "config", ExecutionOperationKind.Replace, ExecutionStepOutcome.NotStarted)]);

        RollbackPlan rollback = RollbackPlanPolicy.Create(draft, snapshot, validatedBackupAvailable: true);

        Assert.Equal(RollbackPlanStatus.Blocked, rollback.Status);
        Assert.Equal(RollbackBlockerKind.JournalSchemaMismatch, Assert.Single(rollback.Blockers).Kind);
    }

    [Fact]
    public void JournalStructureMismatchBlocksRollback()
    {
        ExecutionJournalDraft draft = Draft(
            new ExecutionJournalEntry(0, "config", ExpectedEntryKind.Directory, ExecutionOperationKind.Replace));
        var snapshot = new ExecutionJournalSnapshot(
            ExecutionJournalDraft.CurrentSchemaVersion,
            [new ExecutionJournalStep(0, "options.txt", ExecutionOperationKind.Replace, ExecutionStepOutcome.NotStarted)]);

        RollbackPlan rollback = RollbackPlanPolicy.Create(draft, snapshot, validatedBackupAvailable: true);

        Assert.Equal(RollbackPlanStatus.Blocked, rollback.Status);
        Assert.Equal(RollbackBlockerKind.JournalStructureMismatch, Assert.Single(rollback.Blockers).Kind);
    }

    [Fact]
    public void FingerprintRejectsMalformedSha()
    {
        Assert.Throws<ArgumentException>(() => new ExecutionContentFingerprint(1, 0, 1, "not-a-sha"));
    }

    private static ExecutionJournalDraft Draft(params ExecutionJournalEntry[] entries) =>
        new(ExecutionJournalStatus.Ready, entries, []);

    private static ExecutionContentFingerprint Fingerprint(string seed) =>
        new(1, 1, 10, string.Concat(Enumerable.Repeat(seed, 64)));
}
