using MinecraftInstanceMigration.Domain.Execution;

namespace MinecraftInstanceMigration.Application.Execution;

public sealed class ExecutionJournalPersistence(IExecutionJournalStorage storage) : IExecutionJournalPersistence
{
    private readonly IExecutionJournalStorage storage =
        storage ?? throw new ArgumentNullException(nameof(storage));

    public Task<ExecutionJournalWriteResult> CreateAsync(
        string journalParent,
        ExecutionJournalDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (!draft.CanStartExecution)
        {
            return Task.FromResult(InvalidWrite(ExecutionJournalPersistenceFailureKind.InvalidDraft));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new ExecutionJournalWriteResult(ExecutionJournalWriteStatus.Cancelled));
        }

        return storage.CreateAsync(journalParent, draft, cancellationToken);
    }

    public Task<ExecutionJournalWriteResult> MarkStepStartedAsync(
        ExecutionJournalReference journal,
        ExecutionJournalDraft draft,
        int sequence,
        CancellationToken cancellationToken = default) =>
        WriteTransitionAsync(
            journal,
            draft,
            sequence,
            cancellationToken,
            () => storage.MarkStepStartedAsync(journal, draft, sequence, cancellationToken));

    public Task<ExecutionJournalWriteResult> MarkStepAppliedAsync(
        ExecutionJournalReference journal,
        ExecutionJournalDraft draft,
        int sequence,
        ExecutionContentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);

        return WriteTransitionAsync(
            journal,
            draft,
            sequence,
            cancellationToken,
            () => storage.MarkStepAppliedAsync(
                journal,
                draft,
                sequence,
                fingerprint,
                cancellationToken));
    }

    public Task<ExecutionJournalWriteResult> MarkStepFailedAsync(
        ExecutionJournalReference journal,
        ExecutionJournalDraft draft,
        int sequence,
        CancellationToken cancellationToken = default) =>
        WriteTransitionAsync(
            journal,
            draft,
            sequence,
            cancellationToken,
            () => storage.MarkStepFailedAsync(journal, draft, sequence, cancellationToken));

    public Task<ExecutionJournalReadResult> LoadAsync(
        ExecutionJournalReference journal,
        ExecutionJournalDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(draft);

        if (!draft.CanStartExecution)
        {
            return Task.FromResult(new ExecutionJournalReadResult(
                ExecutionJournalReadStatus.Invalid,
                FailureKind: ExecutionJournalPersistenceFailureKind.InvalidDraft));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new ExecutionJournalReadResult(ExecutionJournalReadStatus.Cancelled));
        }

        return storage.LoadAsync(journal, draft, cancellationToken);
    }

    private static Task<ExecutionJournalWriteResult> WriteTransitionAsync(
        ExecutionJournalReference journal,
        ExecutionJournalDraft draft,
        int sequence,
        CancellationToken cancellationToken,
        Func<Task<ExecutionJournalWriteResult>> write)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(draft);

        if (!draft.CanStartExecution)
        {
            return Task.FromResult(InvalidWrite(ExecutionJournalPersistenceFailureKind.InvalidDraft));
        }

        if (sequence < 0 || sequence >= draft.Entries.Count)
        {
            return Task.FromResult(InvalidWrite(ExecutionJournalPersistenceFailureKind.JournalStateInvalid));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new ExecutionJournalWriteResult(ExecutionJournalWriteStatus.Cancelled));
        }

        return write();
    }

    private static ExecutionJournalWriteResult InvalidWrite(
        ExecutionJournalPersistenceFailureKind kind) =>
        new(ExecutionJournalWriteStatus.Invalid, FailureKind: kind);
}
