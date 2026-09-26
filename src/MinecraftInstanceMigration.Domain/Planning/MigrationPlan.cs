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
        IEnumerable<string> unknownSelections,
        IEnumerable<ConflictDecisionIssue>? conflictDecisionIssues = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(unknownSelections);

        SourceRootState = sourceRootState;
        DestinationRootState = destinationRootState;
        Entries = Array.AsReadOnly(entries.ToArray());
        UnknownSelections = Array.AsReadOnly(unknownSelections.ToArray());
        ConflictDecisionIssues = Array.AsReadOnly((conflictDecisionIssues ?? []).ToArray());
    }

    public EntryState SourceRootState { get; }

    public EntryState DestinationRootState { get; }

    public IReadOnlyList<MigrationPlanEntry> Entries { get; }

    public IReadOnlyList<string> UnknownSelections { get; }

    public IReadOnlyList<ConflictDecisionIssue> ConflictDecisionIssues { get; }

    public MigrationPlanStatus Status =>
        SourceRootState != EntryState.Directory ||
        DestinationRootState != EntryState.Directory ||
        UnknownSelections.Count > 0 ||
        ConflictDecisionIssues.Count > 0 ||
        Entries.Any(entry => entry.IsBlocked)
            ? MigrationPlanStatus.Blocked
            : Entries.Any(entry => entry.NeedsDecision)
                ? MigrationPlanStatus.NeedsDecision
                : MigrationPlanStatus.Ready;

    public int ReadyToCopyCount => Entries.Count(entry => entry.IsReadyToCopy);

    public int ReadyToReplaceCount => Entries.Count(entry => entry.IsReadyToReplace);

    public int ReadyForWriteCount => Entries.Count(entry => entry.IsReadyForWrite);

    public int ConflictCount => Entries.Count(entry => entry.NeedsDecision);

    public int BlockedCount => Entries.Count(entry => entry.IsBlocked);

    public int SkippedConflictCount => Entries.Count(entry =>
        entry.Disposition == MigrationPlanDisposition.SkippedDestinationConflict);
}
