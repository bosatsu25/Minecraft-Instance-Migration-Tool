using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Domain.Planning;

public enum MigrationPlanDisposition
{
    ExcludedBySelection,
    SourceMissing,
    ReadyToCopy,
    DestinationConflict,
    SkippedDestinationConflict,
    ReadyToReplace,
    BlockedSourceRoot,
    BlockedDestinationRoot,
    BlockedSourceObservation,
    BlockedSourceKindMismatch,
    BlockedSourceReparsePoint,
    BlockedDestinationObservation,
    BlockedDestinationReparsePoint,
    BlockedDestinationKindMismatch,
    ReadyToMerge,
    MergeInspectionRequired,
    BlockedMergeConflict,
}

public sealed record MigrationPlanEntry(
    string Name,
    ExpectedEntryKind ExpectedKind,
    EntryState? SourceState,
    EntryState? DestinationState,
    bool Selected,
    MigrationPlanDisposition Disposition)
{
    // Evidence behind ReadyToMerge or BlockedMergeConflict; null for every other disposition.
    public MergeInspectionResult? MergeInspection { get; init; }

    public bool IsBlocked => Disposition is
        MigrationPlanDisposition.BlockedSourceRoot or
        MigrationPlanDisposition.BlockedDestinationRoot or
        MigrationPlanDisposition.BlockedSourceObservation or
        MigrationPlanDisposition.BlockedSourceKindMismatch or
        MigrationPlanDisposition.BlockedSourceReparsePoint or
        MigrationPlanDisposition.BlockedDestinationObservation or
        MigrationPlanDisposition.BlockedDestinationReparsePoint or
        MigrationPlanDisposition.BlockedDestinationKindMismatch or
        MigrationPlanDisposition.BlockedMergeConflict;

    public bool NeedsDecision => Disposition == MigrationPlanDisposition.DestinationConflict;

    public bool IsReadyToCopy => Disposition == MigrationPlanDisposition.ReadyToCopy;

    public bool IsReadyToReplace => Disposition == MigrationPlanDisposition.ReadyToReplace;

    public bool IsReadyToMerge => Disposition == MigrationPlanDisposition.ReadyToMerge;

    // Merge was chosen, but the read-only merge inspection has not supplied evidence yet.
    public bool NeedsMergeInspection => Disposition == MigrationPlanDisposition.MergeInspectionRequired;

    public bool IsReadyForWrite => IsReadyToCopy || IsReadyToReplace || IsReadyToMerge;

    // Merge backs up the whole destination entry, exactly like Replace.
    public bool RequiresBackup => IsReadyToReplace || IsReadyToMerge;
}
