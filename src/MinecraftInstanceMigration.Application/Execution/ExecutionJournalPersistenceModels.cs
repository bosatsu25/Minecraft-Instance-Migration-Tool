using MinecraftInstanceMigration.Domain.Execution;

namespace MinecraftInstanceMigration.Application.Execution;

public enum ExecutionJournalWriteStatus
{
    Succeeded,
    Cancelled,
    Invalid,
    Failed,
}

public enum ExecutionJournalReadStatus
{
    Loaded,
    Cancelled,
    Invalid,
    Failed,
}

public enum ExecutionJournalPersistenceFailureKind
{
    InvalidDraft,
    InvalidReference,
    InvalidPath,
    ReparsePoint,
    JournalFormatInvalid,
    JournalStateInvalid,
    AccessDenied,
    IoFailure,
}

public sealed record ExecutionJournalReference(
    string JournalId,
    string JournalPath);

public sealed record ExecutionJournalWriteResult(
    ExecutionJournalWriteStatus Status,
    ExecutionJournalReference? Journal = null,
    ExecutionJournalPersistenceFailureKind? FailureKind = null)
{
    public bool IsSuccess => Status == ExecutionJournalWriteStatus.Succeeded;
}

public sealed record ExecutionJournalReadResult(
    ExecutionJournalReadStatus Status,
    ExecutionJournalSnapshot? Snapshot = null,
    ExecutionJournalPersistenceFailureKind? FailureKind = null)
{
    public bool IsLoaded => Status == ExecutionJournalReadStatus.Loaded && Snapshot is not null;
}
