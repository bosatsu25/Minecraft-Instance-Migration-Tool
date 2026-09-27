using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Execution;

namespace MinecraftInstanceMigration.Application.Execution;

public enum RollbackStorageStatus
{
    Applied,
    GuardRejected,
    RecoveryRequired,
}

public enum RollbackStorageFailureKind
{
    InvalidPlan,
    InvalidPath,
    OverlappingRoots,
    DestinationMissing,
    DestinationChanged,
    BackupMissing,
    BackupChanged,
    ReparsePoint,
    AccessDenied,
    VerificationFailed,
    IoFailure,
}

public sealed record RollbackStorageResult(
    RollbackStorageStatus Status,
    RollbackStorageFailureKind? FailureKind = null)
{
    public bool IsApplied => Status == RollbackStorageStatus.Applied;
}

public sealed record RollbackBackupEvidence(
    BackupPlan Plan,
    BackupVerificationSummary Verification);

public interface IRollbackStorage
{
    Task<RollbackStorageResult> ApplyAsync(
        string destinationRoot,
        string? backupRoot,
        RollbackBackupEvidence? backupEvidence,
        RollbackPlanEntry action,
        CancellationToken cancellationToken);
}

public sealed record RollbackExecutionRequest(
    string DestinationRoot,
    string? BackupRoot,
    BackupPlan? BackupPlan,
    RollbackPlan RollbackPlan);

public enum RollbackExecutionStatus
{
    NotRequired,
    Completed,
    Cancelled,
    Blocked,
    RecoveryRequired,
}

public enum RollbackExecutionFailureKind
{
    InvalidPlan,
    BackupRequired,
    BackupInvalid,
    GuardRejected,
    StorageFailure,
    CancelledAfterPartialRollback,
}

public sealed record RollbackExecutionResult(
    RollbackExecutionStatus Status,
    RollbackExecutionFailureKind? FailureKind = null,
    int CompletedActions = 0)
{
    public bool IsComplete =>
        Status is RollbackExecutionStatus.NotRequired or
        RollbackExecutionStatus.Completed;
}
