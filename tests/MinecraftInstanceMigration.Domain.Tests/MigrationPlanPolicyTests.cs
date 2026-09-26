using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Domain.Tests;

public sealed class MigrationPlanPolicyTests
{
    [Fact]
    public void SelectedPresentSourceAndMissingDestinationProducesReadyCopy()
    {
        var plan = MigrationPlanPolicy.Create(
            Inspection(("config", EntryState.Directory)),
            Inspection(),
            ["config"]);

        Assert.Equal(MigrationPlanStatus.Ready, plan.Status);
        Assert.Equal(1, plan.ReadyToCopyCount);
        var config = Entry(plan, "config");
        Assert.True(config.Selected);
        Assert.Equal(EntryState.Directory, config.SourceState);
        Assert.Equal(EntryState.Missing, config.DestinationState);
        Assert.Equal(MigrationPlanDisposition.ReadyToCopy, config.Disposition);
        Assert.All(plan.Entries.Where(entry => entry.Name != "config"),
            entry => Assert.Equal(MigrationPlanDisposition.ExcludedBySelection, entry.Disposition));
    }

    [Fact]
    public void MissingSelectedSourceIsExplicitNoOpAndDoesNotBlock()
    {
        var plan = MigrationPlanPolicy.Create(Inspection(), Inspection(), ["saves"]);

        Assert.Equal(MigrationPlanStatus.Ready, plan.Status);
        Assert.Equal(0, plan.ReadyToCopyCount);
        Assert.Equal(MigrationPlanDisposition.SourceMissing, Entry(plan, "saves").Disposition);
    }

    [Theory]
    [InlineData(EntryState.File)]
    [InlineData(EntryState.Directory)]
    public void ExistingDestinationRequiresDecisionWithoutInventingCollisionPolicy(EntryState destinationState)
    {
        var plan = MigrationPlanPolicy.Create(
            Inspection(("config", EntryState.Directory)),
            Inspection(("config", destinationState)),
            ["config"]);

        Assert.Equal(MigrationPlanStatus.NeedsDecision, plan.Status);
        Assert.Equal(1, plan.ConflictCount);
        Assert.Equal(MigrationPlanDisposition.DestinationConflict, Entry(plan, "config").Disposition);
    }

    [Theory]
    [InlineData(EntryState.ReparsePoint, MigrationPlanDisposition.BlockedSourceReparsePoint)]
    [InlineData(EntryState.Inaccessible, MigrationPlanDisposition.BlockedSourceObservation)]
    [InlineData(EntryState.Unavailable, MigrationPlanDisposition.BlockedSourceObservation)]
    [InlineData(EntryState.InvalidPath, MigrationPlanDisposition.BlockedSourceObservation)]
    public void UnsafeOrUnknownSourceObservationBlocksSelectedEntry(
        EntryState state,
        MigrationPlanDisposition disposition)
    {
        var plan = MigrationPlanPolicy.Create(
            Inspection(("config", state)),
            Inspection(),
            ["config"]);

        Assert.Equal(MigrationPlanStatus.Blocked, plan.Status);
        Assert.Equal(disposition, Entry(plan, "config").Disposition);
    }

    [Fact]
    public void SourceKindMismatchBlocksSelectedEntry()
    {
        var plan = MigrationPlanPolicy.Create(
            Inspection(("config", EntryState.File)),
            Inspection(),
            ["config"]);

        Assert.Equal(MigrationPlanStatus.Blocked, plan.Status);
        Assert.Equal(MigrationPlanDisposition.BlockedSourceKindMismatch, Entry(plan, "config").Disposition);
    }

    [Theory]
    [InlineData(EntryState.ReparsePoint, MigrationPlanDisposition.BlockedDestinationReparsePoint)]
    [InlineData(EntryState.Inaccessible, MigrationPlanDisposition.BlockedDestinationObservation)]
    [InlineData(EntryState.Unavailable, MigrationPlanDisposition.BlockedDestinationObservation)]
    [InlineData(EntryState.InvalidPath, MigrationPlanDisposition.BlockedDestinationObservation)]
    public void UnsafeOrUnknownDestinationObservationBlocksSelectedEntry(
        EntryState state,
        MigrationPlanDisposition disposition)
    {
        var plan = MigrationPlanPolicy.Create(
            Inspection(("config", EntryState.Directory)),
            Inspection(("config", state)),
            ["config"]);

        Assert.Equal(MigrationPlanStatus.Blocked, plan.Status);
        Assert.Equal(disposition, Entry(plan, "config").Disposition);
    }

    [Theory]
    [InlineData(true, EntryState.Missing, MigrationPlanDisposition.BlockedSourceRoot)]
    [InlineData(false, EntryState.File, MigrationPlanDisposition.BlockedDestinationRoot)]
    public void NonDirectoryRootBlocksPlanning(
        bool invalidSource,
        EntryState rootState,
        MigrationPlanDisposition disposition)
    {
        var source = invalidSource
            ? new InstanceInspectionResult(rootState, [])
            : Inspection(("config", EntryState.Directory));
        var destination = invalidSource
            ? Inspection()
            : new InstanceInspectionResult(rootState, []);

        var plan = MigrationPlanPolicy.Create(source, destination, ["config"]);

        Assert.Equal(MigrationPlanStatus.Blocked, plan.Status);
        Assert.Equal(disposition, Entry(plan, "config").Disposition);
    }

    [Fact]
    public void UnknownSelectionIsPreservedAndBlocksPlan()
    {
        var plan = MigrationPlanPolicy.Create(Inspection(), Inspection(), ["config", "../outside", "CONFIG"]);

        Assert.Equal(MigrationPlanStatus.Blocked, plan.Status);
        Assert.Equal(new[] { "../outside", "CONFIG" }, plan.UnknownSelections);
        Assert.Equal(MigrationPlanDisposition.SourceMissing, Entry(plan, "config").Disposition);
    }

    [Fact]
    public void UnselectedIncompleteObservationDoesNotBlockSelectedReadyEntry()
    {
        var plan = MigrationPlanPolicy.Create(
            Inspection(("config", EntryState.Directory), ("saves", EntryState.Inaccessible)),
            Inspection(("saves", EntryState.Unavailable)),
            ["config"]);

        Assert.Equal(MigrationPlanStatus.Ready, plan.Status);
        Assert.Equal(MigrationPlanDisposition.ReadyToCopy, Entry(plan, "config").Disposition);
        Assert.Equal(MigrationPlanDisposition.ExcludedBySelection, Entry(plan, "saves").Disposition);
    }

    [Fact]
    public void BlockerTakesPrecedenceOverConflict()
    {
        var plan = MigrationPlanPolicy.Create(
            Inspection(("config", EntryState.Directory), ("saves", EntryState.ReparsePoint)),
            Inspection(("config", EntryState.Directory)),
            ["config", "saves"]);

        Assert.Equal(MigrationPlanStatus.Blocked, plan.Status);
        Assert.Equal(1, plan.ConflictCount);
        Assert.Equal(1, plan.BlockedCount);
    }

    [Fact]
    public void DuplicateSelectionsDoNotDuplicatePlanEntries()
    {
        var selections = new List<string> { "config", "config" };
        var plan = MigrationPlanPolicy.Create(
            Inspection(("config", EntryState.Directory)),
            Inspection(),
            selections);
        selections.Clear();

        Assert.Equal(KnownEntryCatalog.All.Count, plan.Entries.Count);
        Assert.Single(plan.Entries, entry => entry.Selected);
        Assert.Equal(MigrationPlanDisposition.ReadyToCopy, Entry(plan, "config").Disposition);
    }

    [Fact]
    public void MissingOrDuplicateObservationIsBlockedInsteadOfGuessed()
    {
        var source = new InstanceInspectionResult(
            EntryState.Directory,
            [
                new("config", ExpectedEntryKind.Directory, EntryState.Directory),
                new("config", ExpectedEntryKind.Directory, EntryState.Directory),
            ]);

        var plan = MigrationPlanPolicy.Create(source, Inspection(), ["config"]);

        Assert.Equal(MigrationPlanStatus.Blocked, plan.Status);
        Assert.Equal(MigrationPlanDisposition.BlockedSourceObservation, Entry(plan, "config").Disposition);
    }

    private static MigrationPlanEntry Entry(MigrationPlan plan, string name) =>
        Assert.Single(plan.Entries, entry => entry.Name == name);

    private static InstanceInspectionResult Inspection(params (string Name, EntryState State)[] overrides)
    {
        var states = overrides.ToDictionary(item => item.Name, item => item.State, StringComparer.Ordinal);
        return new InstanceInspectionResult(
            EntryState.Directory,
            KnownEntryCatalog.All.Select(candidate =>
                new EntryObservation(
                    candidate.Name,
                    candidate.ExpectedKind,
                    states.GetValueOrDefault(candidate.Name, EntryState.Missing))));
    }
}
