using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Domain.Execution;

public enum RollbackPlanStatus
{
    NotRequired,
    Ready,
    RecoveryRequired,
    Blocked,
}

public enum RollbackActionKind
{
    DeleteCreatedEntry,
    RestoreFromBackup,
    ManualRecoveryRequired,
}

public enum RollbackRecoveryReason
{
    ExecutionFailed,
    OutcomeUncertain,
    MissingPostWriteFingerprint,
    ValidatedBackupUnavailable,
}

public enum RollbackBlockerKind
{
    JournalDraftNotReady,
    JournalSchemaMismatch,
    JournalStructureMismatch,
}

public sealed record RollbackBlocker(
    RollbackBlockerKind Kind,
    string? EntryName = null);

public sealed record RollbackPlanEntry(
    int Order,
    string Name,
    ExpectedEntryKind ExpectedKind,
    ExecutionOperationKind Operation,
    RollbackActionKind Action,
    ExecutionContentFingerprint? ExpectedCurrentFingerprint = null,
    RollbackRecoveryReason? RecoveryReason = null);

public sealed class RollbackPlan
{
    public RollbackPlan(
        RollbackPlanStatus status,
        IEnumerable<RollbackPlanEntry> entries,
        IEnumerable<RollbackBlocker> blockers)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(blockers);

        Status = status;
        Entries = Array.AsReadOnly(entries.ToArray());
        Blockers = Array.AsReadOnly(blockers.ToArray());
    }

    public RollbackPlanStatus Status { get; }

    public IReadOnlyList<RollbackPlanEntry> Entries { get; }

    public IReadOnlyList<RollbackBlocker> Blockers { get; }

    public bool CanAttemptAutomaticRollback =>
        Status == RollbackPlanStatus.Ready &&
        Entries.Count > 0 &&
        Entries.All(entry => entry.Action != RollbackActionKind.ManualRecoveryRequired);
}
