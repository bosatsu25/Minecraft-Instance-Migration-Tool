namespace MinecraftInstanceMigration.Domain.Planning;

public enum DestinationConflictDecision
{
    Unresolved,
    Skip,
    Replace,
}

public enum ConflictDecisionIssueKind
{
    UnknownEntry,
    NotSelected,
    NoDestinationConflict,
    UnsupportedDecision,
}

public sealed record ConflictDecisionIssue(string Name, ConflictDecisionIssueKind Kind);
