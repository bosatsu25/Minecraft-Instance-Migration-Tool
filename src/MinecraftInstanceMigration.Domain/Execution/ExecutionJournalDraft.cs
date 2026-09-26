using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Domain.Execution;

public enum ExecutionJournalStatus
{
    NotRequired,
    Ready,
    Blocked,
}

public enum ExecutionOperationKind
{
    Copy,
    Replace,
}

public enum ExecutionJournalBlockerKind
{
    MigrationPlanNotReady,
    InvalidWriteIntent,
}

public sealed record ExecutionJournalBlocker(
    ExecutionJournalBlockerKind Kind,
    string? EntryName = null);

public sealed record ExecutionJournalEntry(
    int Sequence,
    string Name,
    ExpectedEntryKind ExpectedKind,
    ExecutionOperationKind Operation);

public sealed class ExecutionJournalDraft
{
    public const int CurrentSchemaVersion = 1;

    public ExecutionJournalDraft(
        ExecutionJournalStatus status,
        IEnumerable<ExecutionJournalEntry> entries,
        IEnumerable<ExecutionJournalBlocker> blockers)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(blockers);

        Status = status;
        Entries = Array.AsReadOnly(entries.ToArray());
        Blockers = Array.AsReadOnly(blockers.ToArray());
    }

    public int SchemaVersion => CurrentSchemaVersion;

    public ExecutionJournalStatus Status { get; }

    public IReadOnlyList<ExecutionJournalEntry> Entries { get; }

    public IReadOnlyList<ExecutionJournalBlocker> Blockers { get; }

    public bool CanStartExecution =>
        Status == ExecutionJournalStatus.Ready &&
        Entries.Count > 0 &&
        Blockers.Count == 0;
}
