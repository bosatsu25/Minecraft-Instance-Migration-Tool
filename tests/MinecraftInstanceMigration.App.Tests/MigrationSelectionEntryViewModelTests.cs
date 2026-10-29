using MinecraftInstanceMigration.App.Presentation;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.App.Tests;

public sealed class MigrationSelectionEntryViewModelTests
{
    [Fact]
    public void BlockedKindMismatchedReplaceCanBeChangedToSkip()
    {
        int changes = 0;
        var model = new MigrationSelectionEntryViewModel(
            Entry(MigrationPlanDisposition.BlockedDestinationKindMismatch),
            DestinationConflictDecision.Replace,
            () => changes++);

        Assert.True(model.CanChooseConflict);
        model.ConflictDecision = DestinationConflictDecision.Skip;

        Assert.Equal(DestinationConflictDecision.Skip, model.ConflictDecision);
        Assert.Equal(1, changes);
    }

    [Theory]
    [InlineData(MigrationPlanDisposition.BlockedDestinationReparsePoint)]
    [InlineData(MigrationPlanDisposition.BlockedDestinationObservation)]
    [InlineData(MigrationPlanDisposition.ReadyToCopy)]
    public void RowsWithoutAnExistingComparableEntryOfferNoConflictChoice(MigrationPlanDisposition disposition)
    {
        int changes = 0;
        var model = new MigrationSelectionEntryViewModel(
            Entry(disposition),
            DestinationConflictDecision.Unresolved,
            () => changes++);

        model.ConflictDecision = DestinationConflictDecision.Replace;

        Assert.False(model.CanChooseConflict);
        Assert.Equal(DestinationConflictDecision.Unresolved, model.ConflictDecision);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void JapaneseExplainsEveryBlockedPlanDisposition()
    {
        foreach (MigrationPlanDisposition disposition in Enum.GetValues<MigrationPlanDisposition>()
                     .Where(value => value.ToString().StartsWith("Blocked", StringComparison.Ordinal)))
        {
            string name = disposition.ToString();
            Assert.NotEqual(name, UiText.Translate(name, japanese: true));
            Assert.Equal(name, UiText.Translate(name, japanese: false));
        }
    }

    [Theory]
    [InlineData("config", true)]
    [InlineData("resourcepacks", true)]
    [InlineData("options.txt", false)]
    [InlineData("saves", false)]
    public void MergeIsOfferedOnlyWhereTheCatalogDefinesAMergeUnit(string name, bool offered)
    {
        int changes = 0;
        var model = new MigrationSelectionEntryViewModel(
            Entry(name, MigrationPlanDisposition.DestinationConflict),
            DestinationConflictDecision.Unresolved,
            () => changes++);

        model.ConflictDecision = DestinationConflictDecision.Merge;

        Assert.True(model.CanChooseConflict);
        Assert.Equal(offered, model.CanChooseMerge);
        Assert.Equal(
            offered ? DestinationConflictDecision.Merge : DestinationConflictDecision.Unresolved,
            model.ConflictDecision);
        Assert.Equal(offered ? 1 : 0, changes);
    }

    [Theory]
    [InlineData(MigrationPlanDisposition.ReadyToMerge)]
    [InlineData(MigrationPlanDisposition.MergeInspectionRequired)]
    [InlineData(MigrationPlanDisposition.BlockedMergeConflict)]
    public void MergeRowsKeepTheConflictChoiceEditable(MigrationPlanDisposition disposition)
    {
        var model = new MigrationSelectionEntryViewModel(
            Entry(disposition),
            DestinationConflictDecision.Merge,
            () => { });

        Assert.True(model.CanChooseConflict);
        Assert.True(model.CanChooseMerge);
        model.ConflictDecision = DestinationConflictDecision.Skip;
        Assert.Equal(DestinationConflictDecision.Skip, model.ConflictDecision);
    }

    private static MigrationPreviewEntry Entry(MigrationPlanDisposition disposition) =>
        Entry("config", disposition);

    private static MigrationPreviewEntry Entry(string name, MigrationPlanDisposition disposition) =>
        new(
            name,
            Selected: true,
            EntryState.Directory,
            EntryState.File,
            MigrationPreviewAction.Blocked,
            disposition,
            RequiresBackup: false);
}
