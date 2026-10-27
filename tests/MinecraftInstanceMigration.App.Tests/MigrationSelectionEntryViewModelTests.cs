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

    private static MigrationPreviewEntry Entry(MigrationPlanDisposition disposition) =>
        new(
            "config",
            Selected: true,
            EntryState.Directory,
            EntryState.File,
            MigrationPreviewAction.Blocked,
            disposition,
            RequiresBackup: false);
}
