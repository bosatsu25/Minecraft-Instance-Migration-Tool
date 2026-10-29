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

// ADR 0004: Merge rebuilds the entry from the validated backup's kept units and the source.
[SupportedOSPlatform("windows")]
public sealed class MergeIntegrationTests
{
    [Fact]
    public async Task FileUnitTakesSharedFilesFromSourceKeepsDestinationOnlyFilesAndExcludedFiles()
    {
        using var fixture = new InspectionFixture();
        MergeFixture merge = await PrepareAsync(fixture, "config", destination =>
        {
            Write(destination, "a.cfg", "old-a");
            Write(destination, "b.cfg", "new-pack-only-b");
            Write(destination, "sub/c.cfg", "kept-c");
            Write(destination, "sub/hanemod-client.json", "destination-account");
            Write(destination, "Foo.cfg", "destination-foo");
        }, source =>
        {
            Write(source, "a.cfg", "src-a");
            Write(source, "sub/d.cfg", "src-d");
            Write(source, "new/e.cfg", "src-e");
            Write(source, "foo.cfg", "src-foo");
            Write(source, "hanemod-client.json", "source-account");
        });

        MergeInspectionResult inspection = await new WindowsMergeInspector().InspectAsync(
            merge.Source, merge.Destination, "config", TestContext.Current.CancellationToken);

        Assert.Equal(MergeInspectionStatus.Clean, inspection.Status);
        Assert.Equal(MergeUnit.File, inspection.Unit);
        Assert.Equal(2, inspection.UnitsAdded);
        Assert.Equal(2, inspection.UnitsReplaced);
        Assert.Equal(2, inspection.UnitsKept);
        Assert.Equal(
            "src-a".Length + "src-d".Length + "src-e".Length + "src-foo".Length +
            "new-pack-only-b".Length + "kept-c".Length,
            inspection.BytesToWrite);

        ExecutionPostWriteVerificationResult verification = await MergeAndVerifyAsync(merge);

        Assert.True(verification.IsVerified, $"{verification.Status}/{verification.FailureKind}");
        Assert.Equal(PreStateFingerprints.Current(merge.Destination, merge.Step), verification.Fingerprint);
        Assert.Equal("src-a", Read(merge.Destination, "a.cfg"));
        Assert.Equal("new-pack-only-b", Read(merge.Destination, "b.cfg"));
        Assert.Equal("kept-c", Read(merge.Destination, "sub/c.cfg"));
        Assert.Equal("src-d", Read(merge.Destination, "sub/d.cfg"));
        Assert.Equal("src-e", Read(merge.Destination, "new/e.cfg"));
        Assert.Equal("destination-account", Read(merge.Destination, "sub/hanemod-client.json"));
        Assert.False(File.Exists(Path.Combine(merge.Destination, "config", "hanemod-client.json")));
        string foo = Assert.Single(
            Directory.GetFiles(Path.Combine(merge.Destination, "config")),
            path => string.Equals(Path.GetFileName(path), "foo.cfg", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("foo.cfg", Path.GetFileName(foo));
        Assert.Equal("src-foo", File.ReadAllText(foo));
    }

    [Fact]
    public async Task TopLevelChildUnitReplacesSharedChildrenWholeAndKeepsTheRest()
    {
        using var fixture = new InspectionFixture();
        MergeFixture merge = await PrepareAsync(fixture, "resourcepacks", destination =>
        {
            Write(destination, "packA/old.txt", "old-pack-a");
            Write(destination, "packB/b.txt", "kept-pack-b");
            Write(destination, "x.zip", "destination-x");
        }, source =>
        {
            Write(source, "packA/new.txt", "src-pack-a");
            Write(source, "x.zip", "src-x");
            Write(source, "y.zip", "src-y");
        });

        MergeInspectionResult inspection = await new WindowsMergeInspector().InspectAsync(
            merge.Source, merge.Destination, "resourcepacks", TestContext.Current.CancellationToken);

        Assert.Equal(MergeInspectionStatus.Clean, inspection.Status);
        Assert.Equal(1, inspection.UnitsAdded);
        Assert.Equal(2, inspection.UnitsReplaced);
        Assert.Equal(1, inspection.UnitsKept);

        ExecutionPostWriteVerificationResult verification = await MergeAndVerifyAsync(merge);

        Assert.True(verification.IsVerified, $"{verification.Status}/{verification.FailureKind}");
        Assert.False(File.Exists(Path.Combine(merge.Destination, "resourcepacks", "packA", "old.txt")));
        Assert.Equal("src-pack-a", Read(merge.Destination, "packA/new.txt"));
        Assert.Equal("kept-pack-b", Read(merge.Destination, "packB/b.txt"));
        Assert.Equal("src-x", Read(merge.Destination, "x.zip"));
        Assert.Equal("src-y", Read(merge.Destination, "y.zip"));
    }

    [Fact]
    public async Task VerifierRejectsAMissingKeptFileAMissingSourceFileAndAnExtraFile()
    {
        using var fixture = new InspectionFixture();
        MergeFixture merge = await PrepareAsync(fixture, "config", destination =>
        {
            Write(destination, "kept.cfg", "kept");
        }, source =>
        {
            Write(source, "added.cfg", "added");
        });
        Assert.True((await MergeAndVerifyAsync(merge)).IsVerified);

        await AssertMismatchAfterAsync(merge, () => File.Delete(EntryPath(merge.Destination, "kept.cfg")),
            () => WriteIn(merge.Destination, "kept.cfg", "kept"));
        await AssertMismatchAfterAsync(merge, () => WriteIn(merge.Destination, "kept.cfg", "edited"),
            () => WriteIn(merge.Destination, "kept.cfg", "kept"));
        await AssertMismatchAfterAsync(merge, () => File.Delete(EntryPath(merge.Destination, "added.cfg")),
            () => WriteIn(merge.Destination, "added.cfg", "added"));
        await AssertMismatchAfterAsync(merge, () => WriteIn(merge.Destination, "extra.cfg", "extra"),
            () => File.Delete(EntryPath(merge.Destination, "extra.cfg")));
    }

    [Fact]
    public async Task KindConflictIsBlockedByInspectionAndRefusedBeforeAnythingIsDeleted()
    {
        using var fixture = new InspectionFixture();
        MergeFixture merge = await PrepareAsync(fixture, "config", destination =>
        {
            Write(destination, "mod", "a file in the destination");
        }, source =>
        {
            Write(source, "mod/settings.cfg", "a directory in the source");
        });

        MergeInspectionResult inspection = await new WindowsMergeInspector().InspectAsync(
            merge.Source, merge.Destination, "config", TestContext.Current.CancellationToken);
        ExecutionMutationResult mutation = await new WindowsExecutionMutationPort().MergeAsync(
            merge.Source,
            merge.Destination,
            merge.BackupRoot,
            merge.Step,
            PreStateFingerprints.Current(merge.Destination, merge.Step),
            TestContext.Current.CancellationToken);

        Assert.Equal(MergeInspectionStatus.KindConflict, inspection.Status);
        Assert.Equal(ExecutionMutationStatus.Failed, mutation.Status);
        Assert.Equal(ExecutionMutationFailureKind.SourceChanged, mutation.FailureKind);
        Assert.Equal("a file in the destination", Read(merge.Destination, "mod"));
    }

    [Fact]
    public async Task MergeRefusesADestinationThatChangedAfterItsPreStateWasProven()
    {
        using var fixture = new InspectionFixture();
        MergeFixture merge = await PrepareAsync(fixture, "config", destination =>
        {
            Write(destination, "kept.cfg", "kept");
        }, source =>
        {
            Write(source, "added.cfg", "added");
        });
        ExecutionContentFingerprint proven = PreStateFingerprints.Current(merge.Destination, merge.Step);
        WriteIn(merge.Destination, "edited-after-proof.cfg", "member-change");

        ExecutionMutationResult mutation = await new WindowsExecutionMutationPort().MergeAsync(
            merge.Source, merge.Destination, merge.BackupRoot, merge.Step, proven, TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionMutationStatus.Failed, mutation.Status);
        Assert.Equal(ExecutionMutationFailureKind.DestinationChanged, mutation.FailureKind);
        Assert.Equal("member-change", Read(merge.Destination, "edited-after-proof.cfg"));
        Assert.False(File.Exists(EntryPath(merge.Destination, "added.cfg")));
    }

    [Fact]
    public async Task InspectionReportsANestedReparsePointWithoutFollowingIt()
    {
        using var fixture = new InspectionFixture();
        Directory.CreateDirectory(fixture.At("source/config"));
        Directory.CreateDirectory(fixture.At("destination/config"));
        Directory.CreateDirectory(fixture.At("outside"));
        File.WriteAllText(fixture.At("outside/secret.txt"), "keep-me");
        fixture.Junction("destination/config/linked", "outside");

        MergeInspectionResult inspection = await new WindowsMergeInspector().InspectAsync(
            fixture.At("source"), fixture.At("destination"), "config", TestContext.Current.CancellationToken);

        Assert.Equal(MergeInspectionStatus.ReparsePoint, inspection.Status);
        Assert.Equal("keep-me", File.ReadAllText(fixture.At("outside/secret.txt")));
    }

    [Fact]
    public async Task OrchestratedMergeIsJournaledAndRollsBackToThePreMergeContent()
    {
        using var fixture = new InspectionFixture();
        MergeFixture merge = await PrepareAsync(fixture, "config", destination =>
        {
            Write(destination, "shared.cfg", "new-pack");
            Write(destination, "pack-only.cfg", "pack-tuned");
        }, source =>
        {
            Write(source, "shared.cfg", "member-setting");
        });
        ExecutionContentFingerprint preMerge = PreStateFingerprints.Current(merge.Destination, merge.Step);
        string journals = fixture.At("journals");
        Directory.CreateDirectory(journals);

        var safety = new ExecutionSafetyPlanner();
        var journal = new ExecutionJournalPersistence(new WindowsExecutionJournalStorage());
        var backupStorage = new WindowsBackupStorage();
        ExecutionOrchestrationResult result = await new ExecutionOrchestrator(
            safety,
            new ExecutionLiveValidator(new InstanceInspector(new WindowsInspectionFileSystem())),
            new BackupPlanner(),
            new BackupArtifactValidator(backupStorage),
            new WindowsExecutionPreStateProbe(),
            new WindowsExecutionWorkspaceSafetyValidator(),
            journal,
            new WindowsExecutionMutationPort(),
            new WindowsExecutionPostWriteVerifier()).ExecuteAsync(
                new ExecutionOrchestrationRequest(
                    merge.Source,
                    merge.Destination,
                    journals,
                    merge.BackupRoot,
                    merge.Plan),
                TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOrchestrationStatus.Completed, result.Status);
        Assert.Equal("member-setting", Read(merge.Destination, "shared.cfg"));
        Assert.Equal("pack-tuned", Read(merge.Destination, "pack-only.cfg"));

        ExecutionJournalReadResult loaded = await journal.LoadAsync(
            result.Journal!,
            safety.CreateJournalDraft(merge.Plan),
            TestContext.Current.CancellationToken);
        ExecutionJournalStep applied = Assert.Single(loaded.Snapshot!.Steps);
        Assert.Equal(ExecutionOperationKind.Merge, applied.Operation);
        Assert.Equal(ExecutionStepOutcome.Applied, applied.Outcome);

        RollbackPlan rollbackPlan = RollbackPlanPolicy.Create(
            safety.CreateJournalDraft(merge.Plan),
            loaded.Snapshot,
            validatedBackupAvailable: true);
        RollbackExecutionResult rollback = await new RollbackExecutor(
            new BackupArtifactValidator(backupStorage),
            new RollbackAttemptPersistence(new WindowsRollbackAttemptStorage()),
            new WindowsRollbackStorage()).ExecuteAsync(
                new RollbackExecutionRequest(
                    merge.Destination,
                    merge.BackupRoot,
                    merge.BackupPlan,
                    journals,
                    rollbackPlan),
                TestContext.Current.CancellationToken);

        Assert.Equal(RollbackExecutionStatus.Completed, rollback.Status);
        Assert.Equal(preMerge, PreStateFingerprints.Current(merge.Destination, merge.Step));
        Assert.Equal("new-pack", Read(merge.Destination, "shared.cfg"));
    }

    private static async Task<ExecutionPostWriteVerificationResult> MergeAndVerifyAsync(MergeFixture merge)
    {
        ExecutionMutationResult mutation = await new WindowsExecutionMutationPort().MergeAsync(
            merge.Source,
            merge.Destination,
            merge.BackupRoot,
            merge.Step,
            PreStateFingerprints.Current(merge.Destination, merge.Step),
            TestContext.Current.CancellationToken);
        Assert.True(mutation.IsApplied, $"{mutation.Status}/{mutation.FailureKind}");

        return await new WindowsExecutionPostWriteVerifier().VerifyMergeAsync(
            merge.Source,
            merge.Destination,
            merge.BackupRoot,
            merge.Step,
            TestContext.Current.CancellationToken);
    }

    private static async Task AssertMismatchAfterAsync(MergeFixture merge, Action tamper, Action repair)
    {
        tamper();
        ExecutionPostWriteVerificationResult verification =
            await new WindowsExecutionPostWriteVerifier().VerifyMergeAsync(
                merge.Source,
                merge.Destination,
                merge.BackupRoot,
                merge.Step,
                TestContext.Current.CancellationToken);
        repair();

        Assert.Equal(ExecutionPostWriteVerificationStatus.Failed, verification.Status);
        Assert.Equal(ExecutionPostWriteVerificationFailureKind.VerificationMismatch, verification.FailureKind);
    }

    private static async Task<MergeFixture> PrepareAsync(
        InspectionFixture fixture,
        string entry,
        Action<string> destinationContent,
        Action<string> sourceContent)
    {
        string source = fixture.At("source");
        string destination = fixture.At("destination");
        string backups = fixture.At("backups");
        Directory.CreateDirectory(Path.Combine(source, entry));
        Directory.CreateDirectory(Path.Combine(destination, entry));
        Directory.CreateDirectory(backups);
        destinationContent(Path.Combine(destination, entry));
        sourceContent(Path.Combine(source, entry));

        MergeUnit unit = Assert.Single(KnownEntryCatalog.All, candidate => candidate.Name == entry).MergeUnit;
        MigrationPlan plan = new(
            EntryState.Directory,
            EntryState.Directory,
            [
                new MigrationPlanEntry(
                    entry,
                    ExpectedEntryKind.Directory,
                    EntryState.Directory,
                    EntryState.Directory,
                    true,
                    MigrationPlanDisposition.ReadyToMerge)
                {
                    MergeInspection = new MergeInspectionResult(
                        entry, unit, MergeInspectionStatus.Clean, 0, 0, 0, 0),
                },
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

        return new MergeFixture(
            source,
            destination,
            backup.BackupRootPath!,
            plan,
            backupPlan,
            new ExecutionJournalEntry(0, entry, ExpectedEntryKind.Directory, ExecutionOperationKind.Merge));
    }

    // Paths below are relative to the merged entry: <root>/<entry>/<relative>.
    private static void Write(string entryDirectory, string relative, string content)
    {
        string path = Path.Combine(entryDirectory, relative.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void WriteIn(string root, string relative, string content) =>
        Write(Directory.GetDirectories(root).Single(), relative, content);

    private static string EntryPath(string root, string relative)
    {
        string entry = Directory.GetDirectories(root).Single();
        return Path.Combine(entry, relative.Replace('/', '\\'));
    }

    private static string Read(string root, string relative) => File.ReadAllText(EntryPath(root, relative));

    private sealed record MergeFixture(
        string Source,
        string Destination,
        string BackupRoot,
        MigrationPlan Plan,
        BackupPlan BackupPlan,
        ExecutionJournalEntry Step);
}
