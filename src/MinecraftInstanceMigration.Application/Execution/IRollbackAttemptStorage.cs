using MinecraftInstanceMigration.Domain.Execution;

namespace MinecraftInstanceMigration.Application.Execution;

public interface IRollbackAttemptStorage
{
    Task<RollbackAttemptWriteResult> CreateAsync(
        string journalParent,
        string destinationRoot,
        string? backupRoot,
        RollbackPlan plan,
        CancellationToken cancellationToken);

    Task<RollbackAttemptWriteResult> MarkActionStartedAsync(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        int order,
        CancellationToken cancellationToken);

    Task<RollbackAttemptWriteResult> MarkActionAppliedAsync(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        int order,
        CancellationToken cancellationToken);

    Task<RollbackAttemptWriteResult> MarkActionGuardRejectedAsync(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        int order,
        RollbackStorageFailureKind failureKind,
        CancellationToken cancellationToken);

    Task<RollbackAttemptWriteResult> MarkActionFailedAsync(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        int order,
        RollbackStorageFailureKind failureKind,
        CancellationToken cancellationToken);

    Task<RollbackAttemptReadResult> LoadAsync(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        CancellationToken cancellationToken);
}
