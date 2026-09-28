using MinecraftInstanceMigration.Application.Execution;

namespace MinecraftInstanceMigration.Application.Reporting;

public enum MigrationReportOverallOutcome
{
    Completed,
    Cancelled,
    Blocked,
    RecoveryRequired,
    Recovered,
    GuardRejected,
    Failed,
    Uncertain,
}

public enum MigrationReportVerificationOutcome
{
    NotRun,
    Succeeded,
    Failed,
    Uncertain,
}

public enum MigrationReportBackupOutcome
{
    NotStarted,
    NotRequired,
    Completed,
    Invalid,
    Cancelled,
    Failed,
}

public sealed record MigrationReportMigrationSummary(
    int SelectedEntryCount,
    int CopyCount,
    int ReplaceCount,
    int SkipCount,
    int ExcludedCount,
    int SourceMissingCount,
    int BlockedCount,
    IReadOnlyList<string> ContentRuleSummaries);

public sealed record MigrationReportExecutionSummary(
    int PlannedWriteCount,
    int AppliedCount,
    int FailedCount,
    int UncertainCount,
    MigrationReportVerificationOutcome VerificationOutcome);

public sealed record MigrationReportBackupSummary(
    MigrationReportBackupOutcome Outcome,
    int ReplaceEntryCount);

public sealed record MigrationReportRecoverySummary(
    MigrationRecoveryDiagnosisStatus DiagnosisStatus,
    int RollbackCandidateCount,
    int DeleteCreatedEntryCount,
    int RestoreFromBackupCount,
    MigrationRecoveryOutcome? RollbackOutcome,
    int? AppliedCount,
    int? GuardRejectedCount,
    int? FailedCount,
    int? UncertainCount);

public sealed record MigrationReport(
    MigrationReportOverallOutcome OverallOutcome,
    MigrationReportMigrationSummary Migration,
    MigrationReportExecutionSummary Execution,
    MigrationReportBackupSummary Backup,
    MigrationReportRecoverySummary? Recovery);

public enum MigrationReportCreationStatus
{
    Created,
    InconsistentEvidence,
}

public enum MigrationReportCreationFailureKind
{
    MissingPreview,
    MissingExecutionEvidence,
    InconsistentExecutionEvidence,
    MissingRecoveryEvidence,
    InconsistentRecoveryEvidence,
    UnsupportedWorkflowState,
}

public sealed record MigrationReportCreationResult(
    MigrationReportCreationStatus Status,
    MigrationReport? Report = null,
    MigrationReportCreationFailureKind? FailureKind = null)
{
    public bool IsCreated => Status == MigrationReportCreationStatus.Created && Report is not null;
}
