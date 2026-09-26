using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Domain.Planning;

public enum MigrationPlanStatus
{
    Ready,
    NeedsDecision,
    Blocked,
}

public sealed class MigrationPlan
{
    public MigrationPlan(
        EntryState sourceRootState,
        EntryState destinationRootState,
        IEnumerable<MigrationPlanEntry> entries,
        IEnumerable<string> unknownSelections)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(unknownSelections);

        SourceRootState = sourceRootState;
        DestinationRootState = destinationRootState;
        Entries = Array.AsReadOnly(entries.ToArray());
        UnknownSelections = Array.AsReadOnly(unknownSelections.ToArray());
    }

    public EntryState SourceRootState { get; }

    public EntryState DestinationRootState { get; }

    public IReadOnlyList<MigrationPlanEntry> Entries { get; }

    public IReadOnlyList<string> UnknownSelections { get; }

    public MigrationPlanStatus Status =>
        SourceRootState != EntryState.Directory ||
        DestinationRootState != EntryState.Directory ||
        UnknownSelections.Count > 0 ||
        Entries.Any(entry => entry.IsBlocked)
            ? MigrationPlanStatus.Blocked
            : Entries.Any(entry => entry.NeedsDecision)
                ? MigrationPlanStatus.NeedsDecision
                : MigrationPlanStatus.Ready;

    public int ReadyToCopyCount => Entries.Count(entry => entry.IsReadyToCopy);

    public int ConflictCount => Entries.Count(entry => entry.NeedsDecision);

    public int BlockedCount => Entries.Count(entry => entry.IsBlocked);
}
