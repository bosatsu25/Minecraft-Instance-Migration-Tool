using MinecraftInstanceMigration.Domain.Execution;

namespace MinecraftInstanceMigration.Application.Execution;

public sealed class RollbackAttemptPersistence(
    IRollbackAttemptStorage storage) : IRollbackAttemptPersistence
{
    private readonly IRollbackAttemptStorage storage =
        storage ?? throw new ArgumentNullException(nameof(storage));

    public Task<RollbackAttemptWriteResult> CreateAsync(
        string journalParent,
        string destinationRoot,
        string? backupRoot,
        RollbackPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.CanAttemptAutomaticRollback)
        {
            return Task.FromResult(InvalidWrite(
                RollbackAttemptPersistenceFailureKind.InvalidPlan));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(
                new RollbackAttemptWriteResult(
                    RollbackAttemptWriteStatus.Cancelled));
        }

        return storage.CreateAsync(
            journalParent,
            destinationRoot,
            backupRoot,
            plan,
            cancellationToken);
    }

    public Task<RollbackAttemptWriteResult> MarkActionStartedAsync(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        int order,
        CancellationToken cancellationToken = default) =>
        WriteTransitionAsync(
            attempt,
            plan,
            order,
            cancellationToken,
            () => storage.MarkActionStartedAsync(
                attempt,
                plan,
                order,
                cancellationToken));

    public Task<RollbackAttemptWriteResult> MarkActionAppliedAsync(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        int order,
        CancellationToken cancellationToken = default) =>
        WriteTransitionAsync(
            attempt,
            plan,
            order,
            cancellationToken,
            () => storage.MarkActionAppliedAsync(
                attempt,
                plan,
                order,
                cancellationToken));

    public Task<RollbackAttemptWriteResult> MarkActionGuardRejectedAsync(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        int order,
        RollbackStorageFailureKind failureKind,
        CancellationToken cancellationToken = default) =>
        WriteTransitionAsync(
            attempt,
            plan,
            order,
            cancellationToken,
            () => storage.MarkActionGuardRejectedAsync(
                attempt,
                plan,
                order,
                failureKind,
                cancellationToken));

    public Task<RollbackAttemptWriteResult> MarkActionFailedAsync(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        int order,
        RollbackStorageFailureKind failureKind,
        CancellationToken cancellationToken = default) =>
        WriteTransitionAsync(
            attempt,
            plan,
            order,
            cancellationToken,
            () => storage.MarkActionFailedAsync(
                attempt,
                plan,
                order,
                failureKind,
                cancellationToken));

    public Task<RollbackAttemptReadResult> LoadAsync(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.CanAttemptAutomaticRollback)
        {
            return Task.FromResult(new RollbackAttemptReadResult(
                RollbackAttemptReadStatus.Invalid,
                FailureKind:
                    RollbackAttemptPersistenceFailureKind.InvalidPlan));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new RollbackAttemptReadResult(
                RollbackAttemptReadStatus.Cancelled));
        }

        return storage.LoadAsync(
            attempt,
            plan,
            cancellationToken);
    }

    private static Task<RollbackAttemptWriteResult> WriteTransitionAsync(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        int order,
        CancellationToken cancellationToken,
        Func<Task<RollbackAttemptWriteResult>> write)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.CanAttemptAutomaticRollback)
        {
            return Task.FromResult(InvalidWrite(
                RollbackAttemptPersistenceFailureKind.InvalidPlan));
        }

        if (order < 0 ||
            order >= plan.Entries.Count ||
            plan.Entries[order].Order != order)
        {
            return Task.FromResult(InvalidWrite(
                RollbackAttemptPersistenceFailureKind.JournalStateInvalid,
                attempt));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new RollbackAttemptWriteResult(
                RollbackAttemptWriteStatus.Cancelled,
                attempt));
        }

        return write();
    }

    private static RollbackAttemptWriteResult InvalidWrite(
        RollbackAttemptPersistenceFailureKind kind,
        RollbackAttemptReference? attempt = null) =>
        new(
            RollbackAttemptWriteStatus.Invalid,
            attempt,
            kind);
}
