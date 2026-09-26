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
}

public sealed record MigrationPlanEntry(
    string Name,
    ExpectedEntryKind ExpectedKind,
    EntryState? SourceState,
    EntryState? DestinationState,
    bool Selected,
    MigrationPlanDisposition Disposition)
{
    public bool IsBlocked => Disposition is
        MigrationPlanDisposition.BlockedSourceRoot or
        MigrationPlanDisposition.BlockedDestinationRoot or
        MigrationPlanDisposition.BlockedSourceObservation or
        MigrationPlanDisposition.BlockedSourceKindMismatch or
        MigrationPlanDisposition.BlockedSourceReparsePoint or
        MigrationPlanDisposition.BlockedDestinationObservation or
        MigrationPlanDisposition.BlockedDestinationReparsePoint;

    public bool NeedsDecision => Disposition == MigrationPlanDisposition.DestinationConflict;

    public bool IsReadyToCopy => Disposition == MigrationPlanDisposition.ReadyToCopy;

    public bool IsReadyToReplace => Disposition == MigrationPlanDisposition.ReadyToReplace;

    public bool IsReadyForWrite => IsReadyToCopy || IsReadyToReplace;

    public bool RequiresBackup => IsReadyToReplace;
}
