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
    // A Replace must name the destination fingerprint proven equal to its backup; the port
    // refuses to delete a destination that no longer matches it. Copy passes null.
    Task<ExecutionMutationResult> ApplyAsync(
        string sourceRoot,
        string destinationRoot,
        ExecutionJournalEntry step,
        ExecutionContentFingerprint? expectedDestination,
        CancellationToken cancellationToken);
}

public enum ExecutionPreStateStatus
{
    Matches,
    Changed,
    Failed,
    Cancelled,
}

public sealed record ExecutionPreStateResult(
    ExecutionPreStateStatus Status,
    ExecutionContentFingerprint? Fingerprint = null)
{
    public bool IsMatch =>
        Status == ExecutionPreStateStatus.Matches &&
        Fingerprint is not null;
}

// Proves, before Started, that a Replace destination still holds exactly the content its
// validated backup preserved. Otherwise a change made after Backup would be deleted unrecoverably.
public interface IExecutionPreStateProbe
{
    Task<ExecutionPreStateResult> CompareWithBackupAsync(
        string destinationRoot,
        string backupRoot,
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
