using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Domain.Planning;

public enum MigrationPreviewAction
{
    Excluded,
    NoSource,
    Copy,
    Skip,
    Replace,
    NeedsDecision,
    Blocked,
}

public sealed record MigrationPreviewEntry(
    string Name,
    bool Selected,
    EntryState? SourceState,
    EntryState? DestinationState,
    MigrationPreviewAction Action,
    MigrationPlanDisposition PlanDisposition,
    bool RequiresBackup);
