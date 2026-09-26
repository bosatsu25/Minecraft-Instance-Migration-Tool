using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Domain.Tests;

public sealed class ExecutionJournalPolicyTests
{
    [Fact]
    public void ReadyCopyAndReplaceBecomeOrderedJournalEntries()
    {
        var plan = new MigrationPlan(
            EntryState.Directory,
            EntryState.Directory,
            [
                Entry("options.txt", ExpectedEntryKind.File, EntryState.File, EntryState.Missing, MigrationPlanDisposition.ReadyToCopy),
                Entry("config", ExpectedEntryKind.Directory, EntryState.Directory, EntryState.Directory, MigrationPlanDisposition.ReadyToReplace),
            ],
            []);

        ExecutionJournalDraft journal = ExecutionJournalPolicy.Create(plan);

        Assert.Equal(ExecutionJournalStatus.Ready, journal.Status);
        Assert.True(journal.CanStartExecution);
        Assert.Equal(2, journal.Entries.Count);
        Assert.Collection(
            journal.Entries,
            entry =>
            {
                Assert.Equal(0, entry.Sequence);
                Assert.Equal("options.txt", entry.Name);
                Assert.Equal(ExecutionOperationKind.Copy, entry.Operation);
            },
            entry =>
            {
                Assert.Equal(1, entry.Sequence);
                Assert.Equal("config", entry.Name);
                Assert.Equal(ExecutionOperationKind.Replace, entry.Operation);
            });
        Assert.Empty(journal.Blockers);
    }

    [Fact]
    public void ReadyPlanWithoutWritesDoesNotRequireExecution()
    {
        var plan = new MigrationPlan(
            EntryState.Directory,
            EntryState.Directory,
            [
                Entry("config", ExpectedEntryKind.Directory, EntryState.Missing, EntryState.Missing, MigrationPlanDisposition.SourceMissing),
            ],
            []);

        ExecutionJournalDraft journal = ExecutionJournalPolicy.Create(plan);

        Assert.Equal(ExecutionJournalStatus.NotRequired, journal.Status);
        Assert.False(journal.CanStartExecution);
        Assert.Empty(journal.Entries);
    }

    [Theory]
    [InlineData(MigrationPlanDisposition.DestinationConflict)]
    [InlineData(MigrationPlanDisposition.BlockedSourceObservation)]
    public void NonReadyMigrationPlanBlocksJournal(MigrationPlanDisposition disposition)
    {
        var plan = new MigrationPlan(
            EntryState.Directory,
            EntryState.Directory,
            [
                Entry("config", ExpectedEntryKind.Directory, EntryState.Directory, EntryState.Directory, disposition),
            ],
            []);

        ExecutionJournalDraft journal = ExecutionJournalPolicy.Create(plan);

        Assert.Equal(ExecutionJournalStatus.Blocked, journal.Status);
        Assert.False(journal.CanStartExecution);
        Assert.Contains(journal.Blockers, blocker => blocker.Kind == ExecutionJournalBlockerKind.MigrationPlanNotReady);
    }

    [Fact]
    public void MalformedCopyIntentFailsClosed()
    {
        var plan = new MigrationPlan(
            EntryState.Directory,
            EntryState.Directory,
            [
                Entry("config", ExpectedEntryKind.Directory, EntryState.Directory, EntryState.Directory, MigrationPlanDisposition.ReadyToCopy),
            ],
            []);

        ExecutionJournalDraft journal = ExecutionJournalPolicy.Create(plan);

        Assert.Equal(ExecutionJournalStatus.Blocked, journal.Status);
        Assert.Contains(
            journal.Blockers,
            blocker => blocker.Kind == ExecutionJournalBlockerKind.InvalidWriteIntent && blocker.EntryName == "config");
    }

    [Fact]
    public void MalformedReplaceIntentFailsClosed()
    {
        var plan = new MigrationPlan(
            EntryState.Directory,
            EntryState.Directory,
            [
                Entry("config", ExpectedEntryKind.Directory, EntryState.File, EntryState.Directory, MigrationPlanDisposition.ReadyToReplace),
            ],
            []);

        ExecutionJournalDraft journal = ExecutionJournalPolicy.Create(plan);

        Assert.Equal(ExecutionJournalStatus.Blocked, journal.Status);
        Assert.Contains(
            journal.Blockers,
            blocker => blocker.Kind == ExecutionJournalBlockerKind.InvalidWriteIntent && blocker.EntryName == "config");
    }

    [Fact]
    public void JournalDraftDefensivelyCopiesCollections()
    {
        var entries = new List<ExecutionJournalEntry>
        {
            new(0, "config", ExpectedEntryKind.Directory, ExecutionOperationKind.Replace),
        };
        var blockers = new List<ExecutionJournalBlocker>();

        var journal = new ExecutionJournalDraft(ExecutionJournalStatus.Ready, entries, blockers);
        entries.Clear();
        blockers.Add(new ExecutionJournalBlocker(ExecutionJournalBlockerKind.MigrationPlanNotReady));

        Assert.Single(journal.Entries);
        Assert.Empty(journal.Blockers);
    }

    private static MigrationPlanEntry Entry(
        string name,
        ExpectedEntryKind expectedKind,
        EntryState sourceState,
        EntryState destinationState,
        MigrationPlanDisposition disposition) =>
        new(name, expectedKind, sourceState, destinationState, true, disposition);
}
