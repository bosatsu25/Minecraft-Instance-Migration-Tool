using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Tests;

public sealed class BackupPlannerTests
{
    [Fact]
    public void ApplicationPlannerExposesDomainBackupPreflightWithoutFilesystemAccess()
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

        var planner = new BackupPlanner();
        BackupPlan backup = planner.CreateBackupPlan(migrationPlan);
        BackupManifestDraft manifest = planner.CreateManifestDraft(backup);

        Assert.Equal(BackupPlanStatus.Ready, backup.Status);
        Assert.Single(backup.Entries);
        Assert.Equal(BackupManifestDraft.CurrentSchemaVersion, manifest.SchemaVersion);
        Assert.Single(manifest.Entries);
    }
}
