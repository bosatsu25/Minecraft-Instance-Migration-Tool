using MinecraftInstanceMigration.Domain.Execution;

namespace MinecraftInstanceMigration.Application.Execution;

public enum ExecutionMutationStatus
{
    Applied,
    Failed,
    Cancelled,
}

public enum ExecutionMutationFailureKind
{
    InvalidPath,
    OverlappingRoots,
    SourceChanged,
    DestinationChanged,
    ReparsePoint,
    AccessDenied,
    IoFailure,
}

public sealed record ExecutionMutationResult(
    ExecutionMutationStatus Status,
    ExecutionMutationFailureKind? FailureKind = null)
{
    public bool IsApplied => Status == ExecutionMutationStatus.Applied;
}

public interface IExecutionMutationPort
{
    Task<ExecutionMutationResult> ApplyAsync(
        string sourceRoot,
        string destinationRoot,
        ExecutionJournalEntry step,
        CancellationToken cancellationToken);
}

public enum ExecutionPostWriteVerificationStatus
{
    Verified,
    Failed,
    Cancelled,
}

public enum ExecutionPostWriteVerificationFailureKind
{
    InvalidPath,
    OverlappingRoots,
    SourceChanged,
    DestinationMissing,
    ReparsePoint,
    VerificationMismatch,
    AccessDenied,
    IoFailure,
}

public sealed record ExecutionPostWriteVerificationResult(
    ExecutionPostWriteVerificationStatus Status,
    ExecutionContentFingerprint? Fingerprint = null,
    ExecutionPostWriteVerificationFailureKind? FailureKind = null)
{
    public bool IsVerified =>
        Status == ExecutionPostWriteVerificationStatus.Verified &&
        Fingerprint is not null;
}

public interface IExecutionPostWriteVerifier
{
    Task<ExecutionPostWriteVerificationResult> VerifyAsync(
        string sourceRoot,
        string destinationRoot,
        ExecutionJournalEntry step,
        CancellationToken cancellationToken);
}
