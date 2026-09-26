using MinecraftInstanceMigration.Domain.Execution;

namespace MinecraftInstanceMigration.Application.Execution;

public interface IExecutionJournalPersistence
{
    Task<ExecutionJournalWriteResult> CreateAsync(
        string journalParent,
        ExecutionJournalDraft draft,
        CancellationToken cancellationToken = default);

    Task<ExecutionJournalWriteResult> MarkStepStartedAsync(
        ExecutionJournalReference journal,
        ExecutionJournalDraft draft,
        int sequence,
        CancellationToken cancellationToken = default);

    Task<ExecutionJournalWriteResult> MarkStepAppliedAsync(
        ExecutionJournalReference journal,
        ExecutionJournalDraft draft,
        int sequence,
        ExecutionContentFingerprint fingerprint,
        CancellationToken cancellationToken = default);

    Task<ExecutionJournalWriteResult> MarkStepFailedAsync(
        ExecutionJournalReference journal,
        ExecutionJournalDraft draft,
        int sequence,
        CancellationToken cancellationToken = default);

    Task<ExecutionJournalReadResult> LoadAsync(
        ExecutionJournalReference journal,
        ExecutionJournalDraft draft,
        CancellationToken cancellationToken = default);
}
