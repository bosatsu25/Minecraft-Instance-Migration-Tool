using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Domain.Execution;

public static class ExecutionJournalPolicy
{
    public static ExecutionJournalDraft Create(MigrationPlan migrationPlan)
    {
        ArgumentNullException.ThrowIfNull(migrationPlan);

        if (migrationPlan.Status != MigrationPlanStatus.Ready)
        {
            return new ExecutionJournalDraft(
                ExecutionJournalStatus.Blocked,
                [],
                [new ExecutionJournalBlocker(ExecutionJournalBlockerKind.MigrationPlanNotReady)]);
        }

        var entries = new List<ExecutionJournalEntry>();
        var blockers = new List<ExecutionJournalBlocker>();

        foreach (MigrationPlanEntry entry in migrationPlan.Entries.Where(candidate => candidate.IsReadyForWrite))
        {
            ExecutionOperationKind operation;
            if (entry.IsReadyToCopy)
            {
                if (!MatchesExpectedSource(entry) || entry.DestinationState != EntryState.Missing)
                {
                    blockers.Add(new ExecutionJournalBlocker(
                        ExecutionJournalBlockerKind.InvalidWriteIntent,
                        entry.Name));
                    continue;
                }

                operation = ExecutionOperationKind.Copy;
            }
            else if (entry.IsReadyToReplace)
            {
                if (!MatchesExpectedSource(entry) ||
                    entry.DestinationState is not EntryState.File and not EntryState.Directory)
                {
                    blockers.Add(new ExecutionJournalBlocker(
                        ExecutionJournalBlockerKind.InvalidWriteIntent,
                        entry.Name));
                    continue;
                }

                operation = ExecutionOperationKind.Replace;
            }
            else
            {
                blockers.Add(new ExecutionJournalBlocker(
                    ExecutionJournalBlockerKind.InvalidWriteIntent,
                    entry.Name));
                continue;
            }

            entries.Add(new ExecutionJournalEntry(
                entries.Count,
                entry.Name,
                entry.ExpectedKind,
                operation));
        }

        if (blockers.Count > 0)
        {
            return new ExecutionJournalDraft(ExecutionJournalStatus.Blocked, entries, blockers);
        }

        return entries.Count == 0
            ? new ExecutionJournalDraft(ExecutionJournalStatus.NotRequired, [], [])
            : new ExecutionJournalDraft(ExecutionJournalStatus.Ready, entries, []);
    }

    private static bool MatchesExpectedSource(MigrationPlanEntry entry) =>
        entry.ExpectedKind switch
        {
            ExpectedEntryKind.File => entry.SourceState == EntryState.File,
            ExpectedEntryKind.Directory => entry.SourceState == EntryState.Directory,
            _ => false,
        };
}
