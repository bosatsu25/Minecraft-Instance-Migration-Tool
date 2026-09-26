using System.Runtime.Versioning;
using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Application.Inspection;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;
using MinecraftInstanceMigration.Infrastructure.Backup;
using MinecraftInstanceMigration.Infrastructure.Execution;
using MinecraftInstanceMigration.Infrastructure.Inspection;

namespace MinecraftInstanceMigration.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public sealed class ExecutionOrchestrationIntegrationTests
{
    [Fact]
    public async Task RealCopyFlowPersistsAppliedJournalAndCopiesContent()
    {
        using var fixture = new InspectionFixture();
        string source = fixture.At("source");
        string destination = fixture.At("destination");
        string journals = fixture.At("journals");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(journals);
        File.WriteAllText(Path.Combine(source, "options.txt"), "source-options");

        MigrationPlan plan = new(
            EntryState.Directory,
            EntryState.Directory,
            [
                new MigrationPlanEntry(
                    "options.txt",
                    ExpectedEntryKind.File,
                    EntryState.File,
                    EntryState.Missing,
                    true,
                    MigrationPlanDisposition.ReadyToCopy),
            ],
            []);

        var safety = new ExecutionSafetyPlanner();
        var journal = new ExecutionJournalPersistence(
            new WindowsExecutionJournalStorage());
        ExecutionOrchestrator orchestrator = CreateOrchestrator(
            safety,
            journal);

        ExecutionOrchestrationResult result = await orchestrator.ExecuteAsync(
            new ExecutionOrchestrationRequest(
                source,
                destination,
                journals,
                BackupRoot: null,
                plan),
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOrchestrationStatus.Completed, result.Status);
        Assert.Equal(1, result.AppliedSteps);
        Assert.Equal(
            "source-options",
            File.ReadAllText(Path.Combine(destination, "options.txt")));
        Assert.NotNull(result.Journal);

        ExecutionJournalReadResult loaded = await journal.LoadAsync(
            result.Journal,
            safety.CreateJournalDraft(plan),
            TestContext.Current.CancellationToken);

        Assert.True(loaded.IsLoaded);
        ExecutionJournalStep step = Assert.Single(loaded.Snapshot!.Steps);
        Assert.Equal(ExecutionStepOutcome.Applied, step.Outcome);
        Assert.NotNull(step.AppliedFingerprint);
    }

    [Fact]
    public async Task RealReplaceFlowRequiresValidatedBackupAndPersistsAppliedJournal()
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

        MigrationPlan plan = new(
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

        var backupPlanner = new BackupPlanner();
        BackupPlan backupPlan = backupPlanner.CreateBackupPlan(plan);
        BackupExecutionResult backup = await new BackupExecutor(
            new WindowsBackupStorage(),
            backupPlanner).ExecuteAsync(
                destination,
                backups,
                backupPlan,
                TestContext.Current.CancellationToken);

        Assert.Equal(BackupExecutionStatus.Completed, backup.Status);
        Assert.NotNull(backup.BackupRootPath);

        var safety = new ExecutionSafetyPlanner();
        var journal = new ExecutionJournalPersistence(
            new WindowsExecutionJournalStorage());
        ExecutionOrchestrator orchestrator = CreateOrchestrator(
            safety,
            journal);

        ExecutionOrchestrationResult result = await orchestrator.ExecuteAsync(
            new ExecutionOrchestrationRequest(
                source,
                destination,
                journals,
                backup.BackupRootPath,
                plan),
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOrchestrationStatus.Completed, result.Status);
        Assert.Equal(1, result.AppliedSteps);
        Assert.False(File.Exists(Path.Combine(destination, "config", "old.json")));
        Assert.Equal(
            "new-value",
            File.ReadAllText(Path.Combine(destination, "config", "new.json")));
        Assert.Equal(
            "old-value",
            File.ReadAllText(Path.Combine(backup.BackupRootPath!, "config", "old.json")));

        ExecutionJournalReadResult loaded = await journal.LoadAsync(
            result.Journal!,
            safety.CreateJournalDraft(plan),
            TestContext.Current.CancellationToken);

        Assert.True(loaded.IsLoaded);
        Assert.Equal(
            ExecutionStepOutcome.Applied,
            Assert.Single(loaded.Snapshot!.Steps).Outcome);
    }

    [Fact]
    public async Task NestedSourceReparseAfterLiveInspectionBecomesDurableFailedRecoveryEvidence()
    {
        using var fixture = new InspectionFixture();
        string source = fixture.At("source");
        string destination = fixture.At("destination");
        string journals = fixture.At("journals");
        Directory.CreateDirectory(Path.Combine(source, "config"));
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(journals);
        Directory.CreateDirectory(fixture.At("outside"));
        File.WriteAllText(fixture.At("outside/secret.txt"), "must-not-copy");
        fixture.Junction("source/config/00-linked", "outside");

        MigrationPlan plan = new(
            EntryState.Directory,
            EntryState.Directory,
            [
                new MigrationPlanEntry(
                    "config",
                    ExpectedEntryKind.Directory,
                    EntryState.Directory,
                    EntryState.Missing,
                    true,
                    MigrationPlanDisposition.ReadyToCopy),
            ],
            []);

        var safety = new ExecutionSafetyPlanner();
        var journal = new ExecutionJournalPersistence(
            new WindowsExecutionJournalStorage());
        ExecutionOrchestrator orchestrator = CreateOrchestrator(
            safety,
            journal);

        ExecutionOrchestrationResult result = await orchestrator.ExecuteAsync(
            new ExecutionOrchestrationRequest(
                source,
                destination,
                journals,
                BackupRoot: null,
                plan),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ExecutionOrchestrationStatus.RecoveryRequired,
            result.Status);
        Assert.Equal(
            ExecutionOrchestrationFailureKind.MutationFailed,
            result.FailureKind);
        Assert.NotNull(result.Journal);

        ExecutionJournalReadResult loaded = await journal.LoadAsync(
            result.Journal,
            safety.CreateJournalDraft(plan),
            TestContext.Current.CancellationToken);

        Assert.True(loaded.IsLoaded);
        Assert.Equal(
            ExecutionStepOutcome.Failed,
            Assert.Single(loaded.Snapshot!.Steps).Outcome);
        Assert.Equal(
            "must-not-copy",
            File.ReadAllText(fixture.At("outside/secret.txt")));
    }

    private static ExecutionOrchestrator CreateOrchestrator(
        IExecutionSafetyPlanner safety,
        IExecutionJournalPersistence journal)
    {
        var inspector = new InstanceInspector(new WindowsInspectionFileSystem());
        var backupStorage = new WindowsBackupStorage();

        return new ExecutionOrchestrator(
            safety,
            new ExecutionLiveValidator(inspector),
            new BackupPlanner(),
            new BackupArtifactValidator(backupStorage),
            journal,
            new WindowsExecutionMutationPort(),
            new WindowsExecutionPostWriteVerifier());
    }
}
