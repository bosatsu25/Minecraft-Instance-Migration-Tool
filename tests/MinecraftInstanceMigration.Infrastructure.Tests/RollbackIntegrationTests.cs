using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
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
    public void HeldRollbackTreeDeniesNestedWritesUntilDeletion()
    {
        using var fixture = new InspectionFixture();
        string destination = fixture.At("destination");
        string nested = fixture.At("destination/config/nested/user.json");
        Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
        File.WriteAllText(nested, "migrated");

        using WindowsExecutionTree.HeldDirectory root =
            WindowsExecutionTree.OpenDirectoryChain(destination, writableFinal: true);
        using WindowsExecutionTree.HeldNodeTree tree =
            WindowsExecutionTree.OpenHeldTree(
                root.Root, "config", forDelete: true);

        Assert.Throws<IOException>(() => File.WriteAllText(nested, "new-user-edit"));
        WindowsExecutionTree.TreeFingerprint before =
            WindowsExecutionTree.FingerprintHeldTree(
                tree, TestContext.Current.CancellationToken);
        WindowsExecutionTree.TreeFingerprint after =
            WindowsExecutionTree.FingerprintHeldTree(
                tree, TestContext.Current.CancellationToken);
        Assert.Equal(before, after);

        WindowsExecutionTree.DeleteHeldTree(tree, () => { });
        tree.Dispose();
        Assert.False(Directory.Exists(fixture.At("destination/config")));
    }

    [Fact]
    public void HeldBackupTreeDeniesNestedWritesDuringRestoreEvidence()
    {
        using var fixture = new InspectionFixture();
        string backup = fixture.At("backup");
        string nested = fixture.At("backup/config/nested/user.json");
        Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
        File.WriteAllText(nested, "original");

        using WindowsExecutionTree.HeldDirectory root =
            WindowsExecutionTree.OpenDirectoryChain(backup, writableFinal: false);
        using WindowsExecutionTree.HeldNodeTree tree =
            WindowsExecutionTree.OpenHeldTree(
                root.Root, "config", forDelete: false);

        WindowsExecutionTree.TreeFingerprint before =
            WindowsExecutionTree.FingerprintHeldTree(
                tree, TestContext.Current.CancellationToken);
        Assert.Throws<IOException>(() => File.WriteAllText(nested, "tampered"));
        WindowsExecutionTree.TreeFingerprint after =
            WindowsExecutionTree.FingerprintHeldTree(
                tree, TestContext.Current.CancellationToken);

        Assert.Equal(before, after);
        Assert.Equal("original", File.ReadAllText(nested));
    }

    [Fact]
    public async Task NestedDeleteAccessDenialIsGuardRejectedBeforeMutation()
    {
        using var fixture = new InspectionFixture();
        string destination = fixture.At("destination");
        string nested = fixture.At("destination/config/nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "user.json"), "migrated");

        WindowsExecutionTree.TreeFingerprint fingerprint;
        using (WindowsExecutionTree.HeldDirectory root =
            WindowsExecutionTree.OpenDirectoryChain(
                destination, writableFinal: false))
        {
            fingerprint = WindowsExecutionTree.FingerprintNode(
                root.Root, "config", ExpectedEntryKind.Directory,
                TestContext.Current.CancellationToken);
        }

        var directory = new DirectoryInfo(nested);
        DirectorySecurity original = directory.GetAccessControl();
        DirectorySecurity denied = directory.GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        denied.AddAccessRule(new FileSystemAccessRule(
            identity.User!, FileSystemRights.DeleteSubdirectoriesAndFiles,
            AccessControlType.Deny));
        try
        {
            directory.SetAccessControl(denied);
            var action = new RollbackPlanEntry(
                0, "config", ExpectedEntryKind.Directory,
                ExecutionOperationKind.Copy,
                RollbackActionKind.DeleteCreatedEntry,
                fingerprint.ToDomain());

            RollbackStorageResult result =
                await new WindowsRollbackStorage().ApplyAsync(
                    destination, null, null, action,
                    TestContext.Current.CancellationToken);

            Assert.Equal(RollbackStorageStatus.GuardRejected, result.Status);
            Assert.True(File.Exists(Path.Combine(nested, "user.json")));
        }
        finally
        {
            denied.SetSecurityDescriptorBinaryForm(
                original.GetSecurityDescriptorBinaryForm(),
                AccessControlSections.Access);
            directory.SetAccessControl(denied);
        }
    }

    [Fact]
    public async Task MissingBackupRootIsReportedAsBackupMissing()
    {
        using var fixture = new InspectionFixture();
        string destination = fixture.At("destination");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "options.txt"), "migrated");
        var backupPlan = new BackupPlan(
            BackupPlanStatus.Ready,
            [new BackupPlanEntry(
                "options.txt", ExpectedEntryKind.File, EntryState.File)],
            []);
        var evidence = new RollbackBackupEvidence(
            backupPlan,
            new BackupVerificationSummary(1, 0, 8, new string('A', 64)));
        var action = new RollbackPlanEntry(
            0, "options.txt", ExpectedEntryKind.File,
            ExecutionOperationKind.Replace,
            RollbackActionKind.RestoreFromBackup,
            new ExecutionContentFingerprint(1, 0, 8, new string('B', 64)));

        RollbackStorageResult result =
            await new WindowsRollbackStorage().ApplyAsync(
                destination, fixture.At("missing-backup"), evidence, action,
                TestContext.Current.CancellationToken);

        Assert.Equal(RollbackStorageStatus.GuardRejected, result.Status);
        Assert.Equal(RollbackStorageFailureKind.BackupMissing, result.FailureKind);
        Assert.Equal("migrated", File.ReadAllText(
            Path.Combine(destination, "options.txt")));
    }

    [Fact]
    public async Task ExtraBackupContentAfterValidationBlocksRestore()
    {
        using var fixture = new InspectionFixture();
        string destination = fixture.At("destination");
        string backups = fixture.At("backups");
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(backups);
        Directory.CreateDirectory(journals);
        File.WriteAllText(Path.Combine(destination, "options.txt"), "original");
        var backupPlan = new BackupPlan(
            BackupPlanStatus.Ready,
            [new BackupPlanEntry(
                "options.txt", ExpectedEntryKind.File, EntryState.File)],
            []);
        var backupStorage = new WindowsBackupStorage();
        BackupExecutionResult created = await new BackupExecutor(
            backupStorage, new BackupPlanner()).ExecuteAsync(
                destination, backups, backupPlan,
                TestContext.Current.CancellationToken);
        Assert.Equal(BackupExecutionStatus.Completed, created.Status);

        File.WriteAllText(Path.Combine(destination, "options.txt"), "migrated");
        BackupArtifactValidationResult validation =
            await new BackupArtifactValidator(backupStorage).ValidateAsync(
                created.BackupRootPath!, backupPlan,
                TestContext.Current.CancellationToken);
        Assert.True(validation.IsValid);

        WindowsExecutionTree.TreeFingerprint migrated;
        using (WindowsExecutionTree.HeldDirectory root =
            WindowsExecutionTree.OpenDirectoryChain(
                destination, writableFinal: false))
        {
            migrated = WindowsExecutionTree.FingerprintNode(
                root.Root, "options.txt", ExpectedEntryKind.File,
                TestContext.Current.CancellationToken);
        }

        File.WriteAllText(
            Path.Combine(created.BackupRootPath!, "unplanned.txt"),
            "extra");
        var action = new RollbackPlanEntry(
            0, "options.txt", ExpectedEntryKind.File,
            ExecutionOperationKind.Replace,
            RollbackActionKind.RestoreFromBackup,
            migrated.ToDomain());

        RollbackStorageResult result =
            await new WindowsRollbackStorage().ApplyAsync(
                destination, created.BackupRootPath,
                new RollbackBackupEvidence(
                    backupPlan, validation.Verification!),
                action, TestContext.Current.CancellationToken);

        Assert.Equal(RollbackStorageStatus.GuardRejected, result.Status);
        Assert.Equal(RollbackStorageFailureKind.BackupChanged, result.FailureKind);
        Assert.Equal("migrated", File.ReadAllText(
            Path.Combine(destination, "options.txt")));
    }

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
        string journals = fixture.At("journals");
        Directory.CreateDirectory(Path.Combine(source, "config"));
        Directory.CreateDirectory(Path.Combine(destination, "config"));
        Directory.CreateDirectory(backups);
        Directory.CreateDirectory(journals);
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
                new RollbackAttemptPersistence(
                    new WindowsRollbackAttemptStorage()),
                new WindowsRollbackStorage()).ExecuteAsync(
                    new RollbackExecutionRequest(
                        destination,
                        backup.BackupRootPath,
                        backupPlan,
                        journals,
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
        Directory.CreateDirectory(journals);
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
                new RollbackAttemptPersistence(
                    new WindowsRollbackAttemptStorage()),
                new WindowsRollbackStorage()).ExecuteAsync(
                    new RollbackExecutionRequest(
                        destination,
                        backup.BackupRootPath,
                        backupPlan,
                        journals,
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
        Directory.CreateDirectory(journals);
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

        BackupArtifactValidationResult backupValidation =
            await new BackupArtifactValidator(
                new WindowsBackupStorage()).ValidateAsync(
                    backup.BackupRootPath!,
                    backupPlan,
                    TestContext.Current.CancellationToken);
        Assert.True(backupValidation.IsValid);
        Assert.NotNull(backupValidation.Verification);

        var backupEvidence = new RollbackBackupEvidence(
            backupPlan,
            backupValidation.Verification);

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
                backupEvidence,
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
