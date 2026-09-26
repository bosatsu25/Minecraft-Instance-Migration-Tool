using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Execution;

public sealed record ExecutionOrchestrationRequest(
    string SourceRoot,
    string DestinationRoot,
    string JournalParent,
    string? BackupRoot,
    MigrationPlan MigrationPlan);

public enum ExecutionOrchestrationStatus
{
    NotRequired,
    Completed,
    Cancelled,
    Blocked,
    Failed,
    RecoveryRequired,
}

public enum ExecutionOrchestrationFailureKind
{
    InvalidPlan,
    LiveStateChanged,
    BackupRequired,
    BackupInvalid,
    UnsafeWorkspace,
    JournalFailure,
    MutationFailed,
    PostWriteVerificationFailed,
    UnexpectedFailure,
}

public sealed record ExecutionOrchestrationResult(
    ExecutionOrchestrationStatus Status,
    ExecutionOrchestrationFailureKind? FailureKind = null,
    ExecutionJournalReference? Journal = null,
    int AppliedSteps = 0)
{
    public bool IsComplete =>
        Status is ExecutionOrchestrationStatus.NotRequired or
        ExecutionOrchestrationStatus.Completed;
}
