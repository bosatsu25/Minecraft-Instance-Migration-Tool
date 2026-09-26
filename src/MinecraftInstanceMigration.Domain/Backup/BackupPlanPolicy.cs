using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Domain.Backup;

public static class BackupPlanPolicy
{
    public static BackupPlan Create(MigrationPlan migrationPlan)
    {
        ArgumentNullException.ThrowIfNull(migrationPlan);

        if (migrationPlan.Status != MigrationPlanStatus.Ready)
        {
            return new BackupPlan(
                BackupPlanStatus.Blocked,
                [],
                [new BackupBlocker(BackupBlockerKind.MigrationPlanNotReady)]);
        }

        var entries = new List<BackupPlanEntry>();
        var blockers = new List<BackupBlocker>();

        foreach (MigrationPlanEntry entry in migrationPlan.Entries.Where(candidate => candidate.IsReadyToReplace))
        {
            if (entry.DestinationState is not EntryState.File and not EntryState.Directory)
            {
                blockers.Add(new BackupBlocker(
                    BackupBlockerKind.InvalidReplacementDestinationState,
                    entry.Name));
                continue;
            }

            entries.Add(new BackupPlanEntry(
                entry.Name,
                entry.ExpectedKind,
                entry.DestinationState.Value));
        }

        if (blockers.Count > 0)
        {
            return new BackupPlan(BackupPlanStatus.Blocked, entries, blockers);
        }

        return entries.Count == 0
            ? new BackupPlan(BackupPlanStatus.NotRequired, [], [])
            : new BackupPlan(BackupPlanStatus.Ready, entries, []);
    }

    public static BackupManifestDraft CreateManifestDraft(BackupPlan backupPlan)
    {
        ArgumentNullException.ThrowIfNull(backupPlan);

        if (!backupPlan.CanStartBackup)
        {
            throw new InvalidOperationException("A backup manifest draft requires a ready backup plan.");
        }

        return new BackupManifestDraft(
            backupPlan.Entries.Select(entry => new BackupManifestEntryDraft(
                entry.Name,
                entry.ExpectedKind,
                entry.DestinationState)));
    }
}
