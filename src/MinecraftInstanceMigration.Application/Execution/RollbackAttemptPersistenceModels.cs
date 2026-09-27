using MinecraftInstanceMigration.Domain.Execution;

namespace MinecraftInstanceMigration.Application.Execution;

public enum RollbackAttemptWriteStatus
{
    Succeeded,
    Cancelled,
    Invalid,
    Failed,
}

public enum RollbackAttemptReadStatus
{
    Loaded,
    Cancelled,
    Invalid,
    Failed,
}

public enum RollbackAttemptPersistenceFailureKind
{
    InvalidPlan,
    InvalidReference,
    InvalidPath,
    UnsafeWorkspace,
    ReparsePoint,
    JournalFormatInvalid,
    JournalStateInvalid,
    AccessDenied,
    IoFailure,
}

public sealed record RollbackAttemptReference(
    string AttemptId,
    string JournalPath);

public sealed record RollbackAttemptWriteResult(
    RollbackAttemptWriteStatus Status,
    RollbackAttemptReference? Attempt = null,
    RollbackAttemptPersistenceFailureKind? FailureKind = null)
{
    public bool IsSuccess => Status == RollbackAttemptWriteStatus.Succeeded;
}

public enum RollbackAttemptStepOutcome
{
    NotStarted,
    Applied,
    GuardRejected,
    Failed,
    Uncertain,
}

public sealed record RollbackAttemptStep(
    int Order,
    string Name,
    RollbackActionKind Action,
    RollbackAttemptStepOutcome Outcome,
    RollbackStorageFailureKind? FailureKind = null);

public sealed class RollbackAttemptSnapshot
{
    public const int CurrentSchemaVersion = 1;

    public RollbackAttemptSnapshot(
        int schemaVersion,
        IEnumerable<RollbackAttemptStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        SchemaVersion = schemaVersion;
        Steps = Array.AsReadOnly(steps.ToArray());
    }

    public int SchemaVersion { get; }

    public IReadOnlyList<RollbackAttemptStep> Steps { get; }

    public bool HasUncertainAction =>
        Steps.Any(step => step.Outcome == RollbackAttemptStepOutcome.Uncertain);

    public bool RequiresRecovery =>
        Steps.Any(step =>
            step.Outcome is
                RollbackAttemptStepOutcome.Failed or
                RollbackAttemptStepOutcome.Uncertain);
}

public sealed record RollbackAttemptReadResult(
    RollbackAttemptReadStatus Status,
    RollbackAttemptSnapshot? Snapshot = null,
    RollbackAttemptPersistenceFailureKind? FailureKind = null)
{
    public bool IsLoaded =>
        Status == RollbackAttemptReadStatus.Loaded &&
        Snapshot is not null;
}
