using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Domain.Backup;

public enum BackupPlanStatus
{
    NotRequired,
    Ready,
    Blocked,
}

public enum BackupBlockerKind
{
    MigrationPlanNotReady,
    InvalidReplacementDestinationState,
}

public sealed record BackupBlocker(
    BackupBlockerKind Kind,
    string? EntryName = null);

public sealed record BackupPlanEntry(
    string Name,
    ExpectedEntryKind ExpectedKind,
    EntryState DestinationState);

public sealed class BackupPlan
{
    public BackupPlan(
        BackupPlanStatus status,
        IEnumerable<BackupPlanEntry> entries,
        IEnumerable<BackupBlocker> blockers)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(blockers);

        Status = status;
        Entries = Array.AsReadOnly(entries.ToArray());
        Blockers = Array.AsReadOnly(blockers.ToArray());
    }

    public BackupPlanStatus Status { get; }

    public IReadOnlyList<BackupPlanEntry> Entries { get; }

    public IReadOnlyList<BackupBlocker> Blockers { get; }

    public bool RequiresBackup => Entries.Count > 0;

    public bool CanStartBackup => Status == BackupPlanStatus.Ready && Blockers.Count == 0;
}
