namespace MinecraftInstanceMigration.Application.Execution;

public enum ExecutionWorkspaceSafetyStatus
{
    Safe,
    Invalid,
    Cancelled,
}

public enum ExecutionWorkspaceSafetyFailureKind
{
    InvalidPath,
    OverlappingRoots,
    JournalInsideMigrationRoot,
    ReparsePoint,
    AccessDenied,
    IoFailure,
}

public sealed record ExecutionWorkspaceSafetyResult(
    ExecutionWorkspaceSafetyStatus Status,
    ExecutionWorkspaceSafetyFailureKind? FailureKind = null)
{
    public bool IsSafe => Status == ExecutionWorkspaceSafetyStatus.Safe;
}

public interface IExecutionWorkspaceSafetyValidator
{
    Task<ExecutionWorkspaceSafetyResult> ValidateAsync(
        string sourceRoot,
        string destinationRoot,
        string journalParent,
        CancellationToken cancellationToken = default);
}
