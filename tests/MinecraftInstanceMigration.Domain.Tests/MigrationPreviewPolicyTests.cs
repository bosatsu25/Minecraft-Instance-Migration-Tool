using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Domain.Tests;

public sealed class MigrationPreviewPolicyTests
{
    [Theory]
    [InlineData(MigrationPlanDisposition.ExcludedBySelection, MigrationPreviewAction.Excluded)]
    [InlineData(MigrationPlanDisposition.SourceMissing, MigrationPreviewAction.NoSource)]
    [InlineData(MigrationPlanDisposition.ReadyToCopy, MigrationPreviewAction.Copy)]
    [InlineData(MigrationPlanDisposition.DestinationConflict, MigrationPreviewAction.NeedsDecision)]
    [InlineData(MigrationPlanDisposition.SkippedDestinationConflict, MigrationPreviewAction.Skip)]
    [InlineData(MigrationPlanDisposition.ReadyToReplace, MigrationPreviewAction.Replace)]
    [InlineData(MigrationPlanDisposition.BlockedSourceRoot, MigrationPreviewAction.Blocked)]
    [InlineData(MigrationPlanDisposition.BlockedDestinationRoot, MigrationPreviewAction.Blocked)]
    [InlineData(MigrationPlanDisposition.BlockedSourceObservation, MigrationPreviewAction.Blocked)]
    [InlineData(MigrationPlanDisposition.BlockedSourceKindMismatch, MigrationPreviewAction.Blocked)]
    [InlineData(MigrationPlanDisposition.BlockedSourceReparsePoint, MigrationPreviewAction.Blocked)]
    [InlineData(MigrationPlanDisposition.BlockedDestinationObservation, MigrationPreviewAction.Blocked)]
    [InlineData(MigrationPlanDisposition.BlockedDestinationReparsePoint, MigrationPreviewAction.Blocked)]
    public void MapsEveryCurrentPlanDispositionWithoutReplanning(
        MigrationPlanDisposition disposition,
        MigrationPreviewAction expectedAction)
    {
        var plan = Plan(disposition);

        MigrationPreview preview = MigrationPreviewPolicy.Create(plan);

        MigrationPreviewEntry entry = Assert.Single(preview.Entries);
        Assert.Equal(expectedAction, entry.Action);
        Assert.Equal(disposition, entry.PlanDisposition);
    }

    [Fact]
    public void PreviewPreservesPlanStatusIssuesAndBackupIntent()
    {
        var entries = new[]
        {
            new MigrationPlanEntry(
                "config",
                ExpectedEntryKind.Directory,
                EntryState.Directory,
                EntryState.Directory,
                true,
                MigrationPlanDisposition.ReadyToReplace),
            new MigrationPlanEntry(
                "saves",
                ExpectedEntryKind.Directory,
                EntryState.Directory,
                EntryState.Missing,
                false,
                MigrationPlanDisposition.ExcludedBySelection),
        };
        var plan = new MigrationPlan(
            EntryState.Directory,
            EntryState.Directory,
            entries,
            ["UNKNOWN"],
            [new ConflictDecisionIssue("config", ConflictDecisionIssueKind.NoDestinationConflict)]);

        MigrationPreview preview = MigrationPreviewPolicy.Create(plan);

        Assert.Equal(MigrationPlanStatus.Blocked, preview.Status);
        Assert.Equal(new[] { "UNKNOWN" }, preview.UnknownSelections);
        Assert.Single(preview.ConflictDecisionIssues);
        Assert.Equal(1, preview.ReplaceCount);
        Assert.True(preview.RequiresBackup);
        Assert.True(preview.HasPlannedWrites);
    }

    [Fact]
    public void CopySkipAndNoSourceCountsReflectThePlanSnapshot()
    {
        var plan = new MigrationPlan(
            EntryState.Directory,
            EntryState.Directory,
            [
                Entry("options.txt", ExpectedEntryKind.File, MigrationPlanDisposition.ReadyToCopy),
                Entry("config", ExpectedEntryKind.Directory, MigrationPlanDisposition.SkippedDestinationConflict),
                Entry("saves", ExpectedEntryKind.Directory, MigrationPlanDisposition.SourceMissing),
                Entry("screenshots", ExpectedEntryKind.Directory, MigrationPlanDisposition.DestinationConflict),
                Entry("shaderpacks", ExpectedEntryKind.Directory, MigrationPlanDisposition.BlockedSourceObservation),
            ],
            []);

        MigrationPreview preview = MigrationPreviewPolicy.Create(plan);

        Assert.Equal(1, preview.CopyCount);
        Assert.Equal(1, preview.SkipCount);
        Assert.Equal(1, preview.NoSourceCount);
        Assert.Equal(1, preview.NeedsDecisionCount);
        Assert.Equal(1, preview.BlockedCount);
    }

    [Fact]
    public void UnsupportedFutureDispositionFailsClosedInsteadOfInventingPreviewMeaning()
    {
        var plan = Plan((MigrationPlanDisposition)999);

        Assert.Throws<InvalidOperationException>(() => MigrationPreviewPolicy.Create(plan));
    }

    private static MigrationPlan Plan(MigrationPlanDisposition disposition) =>
        new(
            EntryState.Directory,
            EntryState.Directory,
            [
                new MigrationPlanEntry(
                    "config",
                    ExpectedEntryKind.Directory,
                    EntryState.Directory,
                    EntryState.Missing,
                    true,
                    disposition),
            ],
            []);

    private static MigrationPlanEntry Entry(
        string name,
        ExpectedEntryKind expectedKind,
        MigrationPlanDisposition disposition) =>
        new(name, expectedKind, EntryState.Directory, EntryState.Missing, true, disposition);
}
