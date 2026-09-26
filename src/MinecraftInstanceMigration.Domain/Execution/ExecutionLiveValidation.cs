using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Domain.Execution;

public enum ExecutionLiveValidationStatus
{
    Valid,
    Invalid,
}

public enum ExecutionLiveValidationIssueKind
{
    MigrationPlanNotReady,
    JournalStepMismatch,
    SourceRootChanged,
    DestinationRootChanged,
    SourceObservationInvalid,
    DestinationObservationInvalid,
    SourceStateChanged,
    DestinationStateChanged,
}

public sealed record ExecutionLiveValidationIssue(
    ExecutionLiveValidationIssueKind Kind,
    string? EntryName = null);

public sealed class ExecutionLiveValidationResult
{
    public ExecutionLiveValidationResult(
        ExecutionLiveValidationStatus status,
        IEnumerable<ExecutionLiveValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        Status = status;
        Issues = Array.AsReadOnly(issues.ToArray());
    }

    public ExecutionLiveValidationStatus Status { get; }

    public IReadOnlyList<ExecutionLiveValidationIssue> Issues { get; }

    public bool IsValid => Status == ExecutionLiveValidationStatus.Valid && Issues.Count == 0;
}
