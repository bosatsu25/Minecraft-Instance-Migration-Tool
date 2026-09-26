using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Domain.Tests;

public sealed class InspectionResultTests
{
    [Theory]
    [InlineData(EntryState.File, ExpectedEntryKind.File, true)]
    [InlineData(EntryState.Directory, ExpectedEntryKind.Directory, true)]
    [InlineData(EntryState.File, ExpectedEntryKind.Directory, false)]
    [InlineData(EntryState.Directory, ExpectedEntryKind.File, false)]
    [InlineData(EntryState.ReparsePoint, ExpectedEntryKind.Directory, null)]
    [InlineData(EntryState.Missing, ExpectedEntryKind.File, null)]
    public void PreservesTypeMismatchWithoutTreatingLinksOrAbsenceAsTypes(
        EntryState state, ExpectedEntryKind expected, bool? matches)
    {
        Assert.Equal(matches, new EntryObservation("item", expected, state).MatchesExpectedKind);
    }

    [Theory]
    [InlineData(EntryState.Missing, KnownEntriesState.NoneObserved, true)]
    [InlineData(EntryState.File, KnownEntriesState.Present, true)]
    [InlineData(EntryState.Directory, KnownEntriesState.Present, true)]
    [InlineData(EntryState.ReparsePoint, KnownEntriesState.Present, true)]
    [InlineData(EntryState.Inaccessible, KnownEntriesState.Indeterminate, false)]
    [InlineData(EntryState.Unavailable, KnownEntriesState.Indeterminate, false)]
    public void FailedObservationIsNotEvidenceOfAbsence(
        EntryState state, KnownEntriesState summary, bool complete)
    {
        var result = new InstanceInspectionResult(EntryState.Directory,
            [new("config", ExpectedEntryKind.Directory, state)]);
        Assert.Equal(summary, result.KnownEntries);
        Assert.Equal(complete, result.IsComplete);
    }

    [Fact]
    public void ResultOwnsItsObservationSnapshotAndKeepsPartialEvidence()
    {
        var entries = new List<EntryObservation>
        {
            new("config", ExpectedEntryKind.Directory, EntryState.Directory),
            new("saves", ExpectedEntryKind.Directory, EntryState.Inaccessible),
        };
        var result = new InstanceInspectionResult(EntryState.Directory, entries);
        entries.Clear();
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(KnownEntriesState.Present, result.KnownEntries);
        Assert.False(result.IsComplete);
    }

    [Theory]
    [InlineData(EntryState.Missing)]
    [InlineData(EntryState.File)]
    [InlineData(EntryState.Inaccessible)]
    [InlineData(EntryState.ReparsePoint)]
    [InlineData(EntryState.InvalidPath)]
    public void UninspectedRootDoesNotClaimNoKnownEntries(EntryState root)
    {
        var result = new InstanceInspectionResult(root, []);
        Assert.Equal(KnownEntriesState.NotInspected, result.KnownEntries);
        Assert.False(result.IsComplete);
    }
}
