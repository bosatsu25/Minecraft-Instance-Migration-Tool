using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Domain.Tests;

public sealed class BackupPlanPolicyTests
{
    [Fact]
    public void CopyOnlyPlanDoesNotRequireBackup()
    {
        var plan = MigrationPlan(
            Entry("config", MigrationPlanDisposition.ReadyToCopy, EntryState.Missing));

        BackupPlan backup = BackupPlanPolicy.Create(plan);

        Assert.Equal(BackupPlanStatus.NotRequired, backup.Status);
        Assert.False(backup.RequiresBackup);
        Assert.False(backup.CanStartBackup);
        Assert.Empty(backup.Entries);
        Assert.Empty(backup.Blockers);
    }

    [Fact]
    public void ReplaceIntentProducesReadyBackupEntry()
    {
        var plan = MigrationPlan(
            Entry("config", MigrationPlanDisposition.ReadyToReplace, EntryState.Directory));

        BackupPlan backup = BackupPlanPolicy.Create(plan);

        Assert.Equal(BackupPlanStatus.Ready, backup.Status);
        Assert.True(backup.RequiresBackup);
        Assert.True(backup.CanStartBackup);
        BackupPlanEntry entry = Assert.Single(backup.Entries);
        Assert.Equal("config", entry.Name);
        Assert.Equal(ExpectedEntryKind.Directory, entry.ExpectedKind);
        Assert.Equal(EntryState.Directory, entry.DestinationState);
        Assert.Empty(backup.Blockers);
    }

    [Fact]
    public void MultipleReplaceEntriesPreservePlanOrder()
    {
        var plan = MigrationPlan(
            Entry("config", MigrationPlanDisposition.ReadyToReplace, EntryState.Directory),
            new MigrationPlanEntry(
                "options.txt",
                ExpectedEntryKind.File,
                EntryState.File,
                EntryState.File,
                true,
                MigrationPlanDisposition.ReadyToReplace));

        BackupPlan backup = BackupPlanPolicy.Create(plan);

        Assert.Equal(new[] { "config", "options.txt" }, backup.Entries.Select(entry => entry.Name));
    }

    [Theory]
    [InlineData(MigrationPlanStatus.NeedsDecision)]
    [InlineData(MigrationPlanStatus.Blocked)]
    public void NonReadyMigrationPlanBlocksBackup(MigrationPlanStatus expectedStatus)
    {
        MigrationPlan plan = expectedStatus == MigrationPlanStatus.NeedsDecision
            ? MigrationPlan(Entry("config", MigrationPlanDisposition.DestinationConflict, EntryState.Directory))
            : new MigrationPlan(
                EntryState.Directory,
                EntryState.Directory,
                [Entry("config", MigrationPlanDisposition.ReadyToCopy, EntryState.Missing)],
                ["unknown"]);

        Assert.Equal(expectedStatus, plan.Status);

        BackupPlan backup = BackupPlanPolicy.Create(plan);

        Assert.Equal(BackupPlanStatus.Blocked, backup.Status);
        Assert.False(backup.CanStartBackup);
        BackupBlocker blocker = Assert.Single(backup.Blockers);
        Assert.Equal(BackupBlockerKind.MigrationPlanNotReady, blocker.Kind);
    }

    [Theory]
    [InlineData(EntryState.Missing)]
    [InlineData(EntryState.ReparsePoint)]
    [InlineData(EntryState.Inaccessible)]
    [InlineData(EntryState.InvalidPath)]
    [InlineData(EntryState.Unavailable)]
    public void MalformedReplaceIntentFailsClosed(EntryState destinationState)
    {
        var plan = MigrationPlan(
            Entry("config", MigrationPlanDisposition.ReadyToReplace, destinationState));

        BackupPlan backup = BackupPlanPolicy.Create(plan);

        Assert.Equal(BackupPlanStatus.Blocked, backup.Status);
        Assert.False(backup.CanStartBackup);
        BackupBlocker blocker = Assert.Single(backup.Blockers);
        Assert.Equal(BackupBlockerKind.InvalidReplacementDestinationState, blocker.Kind);
        Assert.Equal("config", blocker.EntryName);
    }

    [Fact]
    public void ManifestDraftIsVersionedPathFreeAndDerivedOnlyFromReadyBackupPlan()
    {
        BackupPlan backup = BackupPlanPolicy.Create(MigrationPlan(
            Entry("config", MigrationPlanDisposition.ReadyToReplace, EntryState.Directory)));

        BackupManifestDraft manifest = BackupPlanPolicy.CreateManifestDraft(backup);

        Assert.Equal(1, manifest.SchemaVersion);
        BackupManifestEntryDraft entry = Assert.Single(manifest.Entries);
        Assert.Equal("config", entry.Name);
        Assert.Equal(ExpectedEntryKind.Directory, entry.ExpectedKind);
        Assert.Equal(EntryState.Directory, entry.DestinationState);
    }

    [Fact]
    public void ManifestDraftRejectsNotRequiredOrBlockedPlans()
    {
        BackupPlan notRequired = BackupPlanPolicy.Create(MigrationPlan(
            Entry("config", MigrationPlanDisposition.ReadyToCopy, EntryState.Missing)));
        BackupPlan blocked = BackupPlanPolicy.Create(new MigrationPlan(
            EntryState.Directory,
            EntryState.Directory,
            [Entry("config", MigrationPlanDisposition.DestinationConflict, EntryState.Directory)],
            []));

        Assert.Throws<InvalidOperationException>(() => BackupPlanPolicy.CreateManifestDraft(notRequired));
        Assert.Throws<InvalidOperationException>(() => BackupPlanPolicy.CreateManifestDraft(blocked));
    }

    [Fact]
    public void BackupPlanDefensivelyCopiesCallerCollections()
    {
        var entries = new List<BackupPlanEntry>
        {
            new("config", ExpectedEntryKind.Directory, EntryState.Directory),
        };
        var blockers = new List<BackupBlocker>();

        var backup = new BackupPlan(BackupPlanStatus.Ready, entries, blockers);
        entries.Clear();
        blockers.Add(new BackupBlocker(BackupBlockerKind.MigrationPlanNotReady));

        Assert.Single(backup.Entries);
        Assert.Empty(backup.Blockers);
    }

    private static MigrationPlan MigrationPlan(params MigrationPlanEntry[] entries) =>
        new(EntryState.Directory, EntryState.Directory, entries, []);

    private static MigrationPlanEntry Entry(
        string name,
        MigrationPlanDisposition disposition,
        EntryState destinationState) =>
        new(
            name,
            ExpectedEntryKind.Directory,
            EntryState.Directory,
            destinationState,
            true,
            disposition);
}
