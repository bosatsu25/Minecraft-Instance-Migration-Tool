using MinecraftInstanceMigration.Domain.Execution;

namespace MinecraftInstanceMigration.Application.Execution;

public interface IRollbackAttemptPersistence
{
    Task<RollbackAttemptWriteResult> CreateAsync(
        string journalParent,
        string destinationRoot,
        string? backupRoot,
        RollbackPlan plan,
        CancellationToken cancellationToken = default);

    Task<RollbackAttemptWriteResult> MarkActionStartedAsync(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        int order,
        CancellationToken cancellationToken = default);

    Task<RollbackAttemptWriteResult> MarkActionAppliedAsync(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        int order,
        CancellationToken cancellationToken = default);

    Task<RollbackAttemptWriteResult> MarkActionGuardRejectedAsync(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        int order,
        RollbackStorageFailureKind failureKind,
        CancellationToken cancellationToken = default);

    Task<RollbackAttemptWriteResult> MarkActionFailedAsync(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        int order,
        RollbackStorageFailureKind failureKind,
        CancellationToken cancellationToken = default);

    Task<RollbackAttemptReadResult> LoadAsync(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        CancellationToken cancellationToken = default);
}
