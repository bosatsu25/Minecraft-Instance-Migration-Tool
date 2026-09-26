namespace MinecraftInstanceMigration.Domain.Planning;

public static class MigrationPreviewPolicy
{
    public static MigrationPreview Create(MigrationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var entries = plan.Entries.Select(entry => new MigrationPreviewEntry(
            entry.Name,
            entry.Selected,
            entry.SourceState,
            entry.DestinationState,
            ToPreviewAction(entry.Disposition),
            entry.Disposition,
            entry.RequiresBackup));

        return new MigrationPreview(
            plan.Status,
            entries,
            plan.UnknownSelections,
            plan.ConflictDecisionIssues);
    }

    private static MigrationPreviewAction ToPreviewAction(MigrationPlanDisposition disposition) =>
        disposition switch
        {
            MigrationPlanDisposition.ExcludedBySelection => MigrationPreviewAction.Excluded,
            MigrationPlanDisposition.SourceMissing => MigrationPreviewAction.NoSource,
            MigrationPlanDisposition.ReadyToCopy => MigrationPreviewAction.Copy,
            MigrationPlanDisposition.DestinationConflict => MigrationPreviewAction.NeedsDecision,
            MigrationPlanDisposition.SkippedDestinationConflict => MigrationPreviewAction.Skip,
            MigrationPlanDisposition.ReadyToReplace => MigrationPreviewAction.Replace,
            MigrationPlanDisposition.BlockedSourceRoot or
            MigrationPlanDisposition.BlockedDestinationRoot or
            MigrationPlanDisposition.BlockedSourceObservation or
            MigrationPlanDisposition.BlockedSourceKindMismatch or
            MigrationPlanDisposition.BlockedSourceReparsePoint or
            MigrationPlanDisposition.BlockedDestinationObservation or
            MigrationPlanDisposition.BlockedDestinationReparsePoint => MigrationPreviewAction.Blocked,
            _ => throw new InvalidOperationException($"Unsupported plan disposition: {disposition}."),
        };
}
