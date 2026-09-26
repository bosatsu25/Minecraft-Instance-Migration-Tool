using MinecraftInstanceMigration.Domain.Execution;

namespace MinecraftInstanceMigration.Application.Execution;

public enum ExecutionMutationStatus
{
    Applied,
    Failed,
    Cancelled,
}

public sealed record ExecutionMutationResult(
    ExecutionMutationStatus Status);

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
}

public sealed record ExecutionPostWriteVerificationResult(
    ExecutionPostWriteVerificationStatus Status,
    ExecutionContentFingerprint? Fingerprint = null)
{
    public bool IsVerified =>
        Status == ExecutionPostWriteVerificationStatus.Verified &&
        Fingerprint is not null;
}

public interface IExecutionPostWriteVerifier
{
    Task<ExecutionPostWriteVerificationResult> VerifyAsync(
        string destinationRoot,
        ExecutionJournalEntry step,
        CancellationToken cancellationToken);
}
