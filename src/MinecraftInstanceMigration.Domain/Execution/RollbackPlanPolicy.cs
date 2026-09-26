namespace MinecraftInstanceMigration.Domain.Execution;

public static class RollbackPlanPolicy
{
    public static RollbackPlan Create(
        ExecutionJournalDraft draft,
        ExecutionJournalSnapshot snapshot,
        bool validatedBackupAvailable)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (draft.Status == ExecutionJournalStatus.Blocked)
        {
            return Blocked(RollbackBlockerKind.JournalDraftNotReady);
        }

        if (snapshot.SchemaVersion != ExecutionJournalDraft.CurrentSchemaVersion)
        {
            return Blocked(RollbackBlockerKind.JournalSchemaMismatch);
        }

        if (!StructureMatches(draft, snapshot, out string? mismatchEntry))
        {
            return Blocked(RollbackBlockerKind.JournalStructureMismatch, mismatchEntry);
        }

        if (draft.Status == ExecutionJournalStatus.NotRequired)
        {
            return new RollbackPlan(RollbackPlanStatus.NotRequired, [], []);
        }

        var rollbackEntries = new List<RollbackPlanEntry>();

        foreach (ExecutionJournalStep step in snapshot.Steps.Reverse())
        {
            ExecutionJournalEntry draftEntry = draft.Entries[step.Sequence];

            switch (step.Outcome)
            {
                case ExecutionStepOutcome.NotStarted:
                    break;

                case ExecutionStepOutcome.Applied:
                    if (step.AppliedFingerprint is null)
                    {
                        rollbackEntries.Add(Manual(
                            rollbackEntries.Count,
                            step,
                            draftEntry.ExpectedKind,
                            RollbackRecoveryReason.MissingPostWriteFingerprint));
                        break;
                    }

                    if (step.Operation == ExecutionOperationKind.Replace && !validatedBackupAvailable)
                    {
                        rollbackEntries.Add(Manual(
                            rollbackEntries.Count,
                            step,
                            draftEntry.ExpectedKind,
                            RollbackRecoveryReason.ValidatedBackupUnavailable));
                        break;
                    }

                    rollbackEntries.Add(new RollbackPlanEntry(
                        rollbackEntries.Count,
                        step.Name,
                        draftEntry.ExpectedKind,
                        step.Operation,
                        step.Operation == ExecutionOperationKind.Copy
                            ? RollbackActionKind.DeleteCreatedEntry
                            : RollbackActionKind.RestoreFromBackup,
                        step.AppliedFingerprint));
                    break;

                case ExecutionStepOutcome.Failed:
                    rollbackEntries.Add(Manual(
                        rollbackEntries.Count,
                        step,
                        RollbackRecoveryReason.ExecutionFailed));
                    break;

                case ExecutionStepOutcome.Uncertain:
                    rollbackEntries.Add(Manual(
                        rollbackEntries.Count,
                        step,
                        RollbackRecoveryReason.OutcomeUncertain));
                    break;

                default:
                    return Blocked(RollbackBlockerKind.JournalStructureMismatch, step.Name);
            }
        }

        if (rollbackEntries.Count == 0)
        {
            return new RollbackPlan(RollbackPlanStatus.NotRequired, [], []);
        }

        RollbackPlanStatus status = rollbackEntries.Any(entry =>
            entry.Action == RollbackActionKind.ManualRecoveryRequired)
            ? RollbackPlanStatus.RecoveryRequired
            : RollbackPlanStatus.Ready;

        return new RollbackPlan(status, rollbackEntries, []);
    }

    private static bool StructureMatches(
        ExecutionJournalDraft draft,
        ExecutionJournalSnapshot snapshot,
        out string? mismatchEntry)
    {
        mismatchEntry = null;

        if (draft.Entries.Count != snapshot.Steps.Count)
        {
            return false;
        }

        for (int index = 0; index < draft.Entries.Count; index++)
        {
            ExecutionJournalEntry expected = draft.Entries[index];
            ExecutionJournalStep actual = snapshot.Steps[index];

            if (expected.Sequence != index ||
                actual.Sequence != index ||
                !string.Equals(expected.Name, actual.Name, StringComparison.Ordinal) ||
                expected.Operation != actual.Operation)
            {
                mismatchEntry = actual.Name;
                return false;
            }
        }

        return true;
    }

    private static RollbackPlanEntry Manual(
        int order,
        ExecutionJournalStep step,
        MinecraftInstanceMigration.Domain.Inspection.ExpectedEntryKind expectedKind,
        RollbackRecoveryReason reason) =>
        new(
            order,
            step.Name,
            expectedKind,
            step.Operation,
            RollbackActionKind.ManualRecoveryRequired,
            step.AppliedFingerprint,
            reason);

    private static RollbackPlan Blocked(
        RollbackBlockerKind kind,
        string? entryName = null) =>
        new(
            RollbackPlanStatus.Blocked,
            [],
            [new RollbackBlocker(kind, entryName)]);
}
