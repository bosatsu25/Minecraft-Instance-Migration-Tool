namespace MinecraftInstanceMigration.Application.Backup;

public enum BackupExecutionStatus
{
    NotRequired,
    Completed,
    Cancelled,
    Failed,
}

public enum BackupFailureKind
{
    InvalidPlan,
    InvalidPath,
    OverlappingRoots,
    ReparsePoint,
    SourceChanged,
    VerificationFailed,
    AccessDenied,
    IoFailure,
}

public sealed record BackupVerificationSummary(
    int FileCount,
    int DirectoryCount,
    long TotalBytes,
    string Sha256);

public sealed record BackupExecutionResult(
    BackupExecutionStatus Status,
    string? BackupRootPath = null,
    BackupFailureKind? FailureKind = null,
    int EntriesCopied = 0,
    BackupVerificationSummary? Verification = null)
{
    public bool IsComplete => Status is BackupExecutionStatus.NotRequired or BackupExecutionStatus.Completed;
}
