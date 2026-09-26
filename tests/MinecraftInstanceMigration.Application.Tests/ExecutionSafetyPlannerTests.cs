using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Tests;

public sealed class ExecutionSafetyPlannerTests
{
    [Fact]
    public void CreatesJournalDraftFromMigrationPlan()
    {
        var migrationPlan = new MigrationPlan(
            EntryState.Directory,
            EntryState.Directory,
            [
                new MigrationPlanEntry(
                    "config",
                    ExpectedEntryKind.Directory,
                    EntryState.Directory,
                    EntryState.Directory,
                    true,
                    MigrationPlanDisposition.ReadyToReplace),
            ],
            []);

        ExecutionJournalDraft draft = new ExecutionSafetyPlanner().CreateJournalDraft(migrationPlan);

        Assert.Equal(ExecutionJournalStatus.Ready, draft.Status);
        Assert.Equal(ExecutionOperationKind.Replace, Assert.Single(draft.Entries).Operation);
    }

    [Fact]
    public void ValidBackupEvidenceEnablesAutomaticReplaceRollback()
    {
        var planner = new ExecutionSafetyPlanner();
        ExecutionJournalDraft draft = new(
            ExecutionJournalStatus.Ready,
            [new ExecutionJournalEntry(0, "config", ExpectedEntryKind.Directory, ExecutionOperationKind.Replace)],
            []);
        var snapshot = new ExecutionJournalSnapshot(
            ExecutionJournalDraft.CurrentSchemaVersion,
            [
                new ExecutionJournalStep(
                    0,
                    "config",
                    ExecutionOperationKind.Replace,
                    ExecutionStepOutcome.Applied,
                    new ExecutionContentFingerprint(1, 1, 10, new string('A', 64))),
            ]);
        var backupValidation = new BackupArtifactValidationResult(
            BackupArtifactValidationStatus.Valid,
            Verification: new BackupVerificationSummary(1, 1, 10, new string('B', 64)));

        RollbackPlan rollback = planner.CreateRollbackPlan(draft, snapshot, backupValidation);

        Assert.Equal(RollbackPlanStatus.Ready, rollback.Status);
        Assert.Equal(RollbackActionKind.RestoreFromBackup, Assert.Single(rollback.Entries).Action);
    }

    [Fact]
    public void InvalidBackupEvidenceDoesNotAuthorizeAutomaticReplaceRollback()
    {
        var planner = new ExecutionSafetyPlanner();
        ExecutionJournalDraft draft = new(
            ExecutionJournalStatus.Ready,
            [new ExecutionJournalEntry(0, "config", ExpectedEntryKind.Directory, ExecutionOperationKind.Replace)],
            []);
        var snapshot = new ExecutionJournalSnapshot(
            ExecutionJournalDraft.CurrentSchemaVersion,
            [
                new ExecutionJournalStep(
                    0,
                    "config",
                    ExecutionOperationKind.Replace,
                    ExecutionStepOutcome.Applied,
                    new ExecutionContentFingerprint(1, 1, 10, new string('A', 64))),
            ]);
        var backupValidation = new BackupArtifactValidationResult(
            BackupArtifactValidationStatus.Invalid,
            BackupArtifactFailureKind.VerificationMismatch);

        RollbackPlan rollback = planner.CreateRollbackPlan(draft, snapshot, backupValidation);

        Assert.Equal(RollbackPlanStatus.RecoveryRequired, rollback.Status);
        Assert.Equal(
            RollbackRecoveryReason.ValidatedBackupUnavailable,
            Assert.Single(rollback.Entries).RecoveryReason);
    }
}
