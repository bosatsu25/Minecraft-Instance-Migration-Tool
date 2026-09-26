using System.Runtime.Versioning;
using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;
using MinecraftInstanceMigration.Infrastructure.Backup;
using MinecraftInstanceMigration.Infrastructure.Execution;

namespace MinecraftInstanceMigration.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public sealed class RollbackIntegrationTests
{
    [Fact]
    public async Task AppliedCopyCanBeDeletedOnlyWhileFingerprintStillMatches()
    {
        using var fixture = new InspectionFixture();
        string source = fixture.At("source");
        string destination = fixture.At("destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(source, "options.txt"), "copied");

        var step = new ExecutionJournalEntry(
            0,
            "options.txt",
            ExpectedEntryKind.File,
            ExecutionOperationKind.Copy);

        Assert.True((await new WindowsExecutionMutationPort().ApplyAsync(
            source,
            destination,
            step,
            TestContext.Current.CancellationToken)).IsApplied);

        ExecutionPostWriteVerificationResult verification =
            await new WindowsExecutionPostWriteVerifier().VerifyAsync(
                source,
                destination,
                step,
                TestContext.Current.CancellationToken);
        Assert.True(verification.IsVerified);

        var action = new RollbackPlanEntry(
            0,
            step.Name,
            step.ExpectedKind,
            step.Operation,
            RollbackActionKind.DeleteCreatedEntry,
            verification.Fingerprint);

        RollbackStorageResult rollback =
            await new WindowsRollbackStorage().ApplyAsync(
                destination,
                null,
                action,
                TestContext.Current.CancellationToken);

        Assert.True(rollback.IsApplied);
        Assert.False(File.Exists(Path.Combine(destination, "options.txt")));
        Assert.Equal("copied", File.ReadAllText(Path.Combine(source, "options.txt")));
    }

    [Fact]
    public async Task EditedCopiedEntryIsNeverDeletedByRollback()
    {
        using var fixture = new InspectionFixture();
        string source = fixture.At("source");
        string destination = fixture.At("destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(source, "options.txt"), "copied");

        var step = new ExecutionJournalEntry(
            0,
            "options.txt",
            ExpectedEntryKind.File,
            ExecutionOperationKind.Copy);

        Assert.True((await new WindowsExecutionMutationPort().ApplyAsync(
            source,
            destination,
            step,
            TestContext.Current.CancellationToken)).IsApplied);

        ExecutionPostWriteVerificationResult verification =
            await new WindowsExecutionPostWriteVerifier().VerifyAsync(
                source,
                destination,
                step,
                TestContext.Current.CancellationToken);
        Assert.True(verification.IsVerified);

        File.WriteAllText(
            Path.Combine(destination, "options.txt"),
            "edited-after-migration");

        var action = new RollbackPlanEntry(
            0,
            step.Name,
            step.ExpectedKind,
            step.Operation,
            RollbackActionKind.DeleteCreatedEntry,
            verification.Fingerprint);

        RollbackStorageResult rollback =
            await new WindowsRollbackStorage().ApplyAsync(
                destination,
                null,
                action,
                TestContext.Current.CancellationToken);

        Assert.Equal(RollbackStorageStatus.GuardRejected, rollback.Status);
        Assert.Equal(
            RollbackStorageFailureKind.DestinationChanged,
            rollback.FailureKind);
        Assert.Equal(
            "edited-after-migration",
            File.ReadAllText(Path.Combine(destination, "options.txt")));
    }

    [Fact]
    public async Task ReplaceRollbackRestoresValidatedBackupEndToEnd()
    {
        using var fixture = new InspectionFixture();
        string source = fixture.At("source");
        string destination = fixture.At("destination");
        string backups = fixture.At("backups");
        Directory.CreateDirectory(Path.Combine(source, "config"));
        Directory.CreateDirectory(Path.Combine(destination, "config"));
        Directory.CreateDirectory(backups);
        File.WriteAllText(Path.Combine(source, "config", "new.json"), "new-value");
        File.WriteAllText(Path.Combine(destination, "config", "old.json"), "old-value");

        MigrationPlan migrationPlan = ReplacePlan();
        var backupPlanner = new BackupPlanner();
        BackupPlan backupPlan = backupPlanner.CreateBackupPlan(migrationPlan);
        var backupStorage = new WindowsBackupStorage();

        BackupExecutionResult backup = await new BackupExecutor(
            backupStorage,
            backupPlanner).ExecuteAsync(
                destination,
                backups,
                backupPlan,
                TestContext.Current.CancellationToken);

        Assert.Equal(BackupExecutionStatus.Completed, backup.Status);
        Assert.NotNull(backup.BackupRootPath);

        var step = new ExecutionJournalEntry(
            0,
            "config",
            ExpectedEntryKind.Directory,
            ExecutionOperationKind.Replace);

        Assert.True((await new WindowsExecutionMutationPort().ApplyAsync(
            source,
            destination,
            step,
            TestContext.Current.CancellationToken)).IsApplied);

        ExecutionPostWriteVerificationResult verification =
            await new WindowsExecutionPostWriteVerifier().VerifyAsync(
                source,
                destination,
                step,
                TestContext.Current.CancellationToken);
        Assert.True(verification.IsVerified);

        ExecutionJournalDraft draft =
            new ExecutionSafetyPlanner().CreateJournalDraft(migrationPlan);
        var snapshot = new ExecutionJournalSnapshot(
            ExecutionJournalDraft.CurrentSchemaVersion,
            [
                new ExecutionJournalStep(
                    0,
                    "config",
                    ExecutionOperationKind.Replace,
                    ExecutionStepOutcome.Applied,
                    verification.Fingerprint),
            ]);

        var validator = new BackupArtifactValidator(backupStorage);
        BackupArtifactValidationResult validation =
            await validator.ValidateAsync(
                backup.BackupRootPath!,
                backupPlan,
                TestContext.Current.CancellationToken);
        Assert.True(validation.IsValid);

        RollbackPlan rollbackPlan =
            new ExecutionSafetyPlanner().CreateRollbackPlan(
                draft,
                snapshot,
                validation);

        RollbackExecutionResult result =
            await new RollbackExecutor(
                validator,
                new WindowsRollbackStorage()).ExecuteAsync(
                    new RollbackExecutionRequest(
                        destination,
                        backup.BackupRootPath,
                        backupPlan,
                        rollbackPlan),
                    TestContext.Current.CancellationToken);

        Assert.Equal(RollbackExecutionStatus.Completed, result.Status);
        Assert.Equal(1, result.CompletedActions);
        Assert.False(File.Exists(Path.Combine(destination, "config", "new.json")));
        Assert.Equal(
            "old-value",
            File.ReadAllText(Path.Combine(destination, "config", "old.json")));
        Assert.Equal(
            "old-value",
            File.ReadAllText(Path.Combine(
                backup.BackupRootPath!,
                "config",
                "old.json")));
    }

    [Fact]
    public async Task TamperedBackupBlocksRestoreBeforeDestinationMutation()
    {
        using var fixture = new InspectionFixture();
        string source = fixture.At("source");
        string destination = fixture.At("destination");
        string backups = fixture.At("backups");
        Directory.CreateDirectory(Path.Combine(source, "config"));
        Directory.CreateDirectory(Path.Combine(destination, "config"));
        Directory.CreateDirectory(backups);
        File.WriteAllText(Path.Combine(source, "config", "new.json"), "new-value");
        File.WriteAllText(Path.Combine(destination, "config", "old.json"), "old-value");

        MigrationPlan migrationPlan = ReplacePlan();
        var backupPlanner = new BackupPlanner();
        BackupPlan backupPlan = backupPlanner.CreateBackupPlan(migrationPlan);
        var backupStorage = new WindowsBackupStorage();
        BackupExecutionResult backup = await new BackupExecutor(
            backupStorage,
            backupPlanner).ExecuteAsync(
                destination,
                backups,
                backupPlan,
                TestContext.Current.CancellationToken);
        Assert.Equal(BackupExecutionStatus.Completed, backup.Status);

        var step = new ExecutionJournalEntry(
            0,
            "config",
            ExpectedEntryKind.Directory,
            ExecutionOperationKind.Replace);
        Assert.True((await new WindowsExecutionMutationPort().ApplyAsync(
            source,
            destination,
            step,
            TestContext.Current.CancellationToken)).IsApplied);

        ExecutionPostWriteVerificationResult verification =
            await new WindowsExecutionPostWriteVerifier().VerifyAsync(
                source,
                destination,
                step,
                TestContext.Current.CancellationToken);
        Assert.True(verification.IsVerified);

        File.WriteAllText(
            Path.Combine(backup.BackupRootPath!, "config", "old.json"),
            "tampered-backup");

        var rollbackPlan = new RollbackPlan(
            RollbackPlanStatus.Ready,
            [
                new RollbackPlanEntry(
                    0,
                    "config",
                    ExpectedEntryKind.Directory,
                    ExecutionOperationKind.Replace,
                    RollbackActionKind.RestoreFromBackup,
                    verification.Fingerprint),
            ],
            []);

        RollbackExecutionResult result =
            await new RollbackExecutor(
                new BackupArtifactValidator(backupStorage),
                new WindowsRollbackStorage()).ExecuteAsync(
                    new RollbackExecutionRequest(
                        destination,
                        backup.BackupRootPath,
                        backupPlan,
                        rollbackPlan),
                    TestContext.Current.CancellationToken);

        Assert.Equal(RollbackExecutionStatus.Blocked, result.Status);
        Assert.Equal(RollbackExecutionFailureKind.BackupInvalid, result.FailureKind);
        Assert.Equal(
            "new-value",
            File.ReadAllText(Path.Combine(destination, "config", "new.json")));
    }

    [Fact]
    public async Task NestedBackupReparseIsRejectedBeforeDestinationDeletion()
    {
        using var fixture = new InspectionFixture();
        string source = fixture.At("source");
        string destination = fixture.At("destination");
        string backups = fixture.At("backups");
        Directory.CreateDirectory(Path.Combine(source, "config"));
        Directory.CreateDirectory(Path.Combine(destination, "config"));
        Directory.CreateDirectory(backups);
        Directory.CreateDirectory(fixture.At("outside"));
        File.WriteAllText(Path.Combine(source, "config", "new.json"), "new-value");
        File.WriteAllText(Path.Combine(destination, "config", "old.json"), "old-value");
        File.WriteAllText(fixture.At("outside/secret.txt"), "keep-me");

        MigrationPlan migrationPlan = ReplacePlan();
        var backupPlanner = new BackupPlanner();
        BackupPlan backupPlan = backupPlanner.CreateBackupPlan(migrationPlan);
        BackupExecutionResult backup = await new BackupExecutor(
            new WindowsBackupStorage(),
            backupPlanner).ExecuteAsync(
                destination,
                backups,
                backupPlan,
                TestContext.Current.CancellationToken);
        Assert.Equal(BackupExecutionStatus.Completed, backup.Status);

        var step = new ExecutionJournalEntry(
            0,
            "config",
            ExpectedEntryKind.Directory,
            ExecutionOperationKind.Replace);
        Assert.True((await new WindowsExecutionMutationPort().ApplyAsync(
            source,
            destination,
            step,
            TestContext.Current.CancellationToken)).IsApplied);
        ExecutionPostWriteVerificationResult verification =
            await new WindowsExecutionPostWriteVerifier().VerifyAsync(
                source,
                destination,
                step,
                TestContext.Current.CancellationToken);
        Assert.True(verification.IsVerified);

        string backupRelative = Path.GetRelativePath(
            fixture.Root,
            backup.BackupRootPath!);
        fixture.Junction(
            Path.Combine(backupRelative, "config", "00-linked"),
            "outside");

        var action = new RollbackPlanEntry(
            0,
            "config",
            ExpectedEntryKind.Directory,
            ExecutionOperationKind.Replace,
            RollbackActionKind.RestoreFromBackup,
            verification.Fingerprint);

        RollbackStorageResult rollback =
            await new WindowsRollbackStorage().ApplyAsync(
                destination,
                backup.BackupRootPath,
                action,
                TestContext.Current.CancellationToken);

        Assert.Equal(RollbackStorageStatus.GuardRejected, rollback.Status);
        Assert.Equal(RollbackStorageFailureKind.ReparsePoint, rollback.FailureKind);
        Assert.Equal(
            "new-value",
            File.ReadAllText(Path.Combine(destination, "config", "new.json")));
        Assert.Equal("keep-me", File.ReadAllText(fixture.At("outside/secret.txt")));
    }

    private static MigrationPlan ReplacePlan() =>
        new(
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
}
