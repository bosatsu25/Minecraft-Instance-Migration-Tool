namespace MinecraftInstanceMigration.Domain.Execution;

public enum ExecutionStepOutcome
{
    NotStarted,
    Applied,
    Failed,
    Uncertain,
}

public sealed record ExecutionJournalStep(
    int Sequence,
    string Name,
    ExecutionOperationKind Operation,
    ExecutionStepOutcome Outcome,
    ExecutionContentFingerprint? AppliedFingerprint = null);

public sealed class ExecutionJournalSnapshot
{
    public ExecutionJournalSnapshot(
        int schemaVersion,
        IEnumerable<ExecutionJournalStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        SchemaVersion = schemaVersion;
        Steps = Array.AsReadOnly(steps.ToArray());
    }

    public int SchemaVersion { get; }

    public IReadOnlyList<ExecutionJournalStep> Steps { get; }
}
