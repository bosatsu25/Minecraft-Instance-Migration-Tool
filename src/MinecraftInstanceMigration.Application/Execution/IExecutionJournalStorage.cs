using MinecraftInstanceMigration.Domain.Execution;

namespace MinecraftInstanceMigration.Application.Execution;

public interface IExecutionJournalStorage
{
    Task<ExecutionJournalWriteResult> CreateAsync(
        string journalParent,
        ExecutionJournalDraft draft,
        CancellationToken cancellationToken);

    Task<ExecutionJournalWriteResult> MarkStepStartedAsync(
        ExecutionJournalReference journal,
        ExecutionJournalDraft draft,
        int sequence,
        CancellationToken cancellationToken);

    Task<ExecutionJournalWriteResult> MarkStepAppliedAsync(
        ExecutionJournalReference journal,
        ExecutionJournalDraft draft,
        int sequence,
        ExecutionContentFingerprint fingerprint,
        CancellationToken cancellationToken);

    Task<ExecutionJournalWriteResult> MarkStepFailedAsync(
        ExecutionJournalReference journal,
        ExecutionJournalDraft draft,
        int sequence,
        CancellationToken cancellationToken);

    Task<ExecutionJournalReadResult> LoadAsync(
        ExecutionJournalReference journal,
        ExecutionJournalDraft draft,
        CancellationToken cancellationToken);
}
