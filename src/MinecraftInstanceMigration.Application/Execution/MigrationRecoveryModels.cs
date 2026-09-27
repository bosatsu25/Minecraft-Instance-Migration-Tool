using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Execution;

public sealed record MigrationRecoveryRequest(
    string DestinationRoot,
    string? BackupRoot,
    BackupPlan BackupPlan,
    string JournalParent,
    MigrationPlan MigrationPlan,
    ExecutionJournalReference ExecutionJournal);

public enum MigrationRecoveryDiagnosisStatus
{
    RollbackAvailable,
    NotRequired,
    ManualRecoveryRequired,
    Blocked,
    Cancelled,
}

public enum MigrationRecoveryDiagnosisFailureKind
{
    InvalidRequest,
    JournalUnavailable,
    BackupUnavailable,
    InvalidEvidence,
}

public sealed record MigrationRecoveryDiagnosis(
    MigrationRecoveryDiagnosisStatus Status,
    RollbackPlan? RollbackPlan = null,
    MigrationRecoveryDiagnosisFailureKind? FailureKind = null,
    int AppliedExecutionSteps = 0,
    int FailedExecutionSteps = 0,
    int UncertainExecutionSteps = 0,
    int NotStartedExecutionSteps = 0,
    int DeleteCreatedEntryCount = 0,
    int RestoreFromBackupCount = 0,
    bool BackupRequired = false)
{
    internal Guid AuthorizationId { get; init; }

    internal MigrationRecoveryRequest? AuthorizedRequest { get; init; }

    public bool CanRollback =>
        Status == MigrationRecoveryDiagnosisStatus.RollbackAvailable &&
        RollbackPlan?.CanAttemptAutomaticRollback == true;

    public int RollbackCandidateCount =>
        DeleteCreatedEntryCount + RestoreFromBackupCount;
}

public enum MigrationRecoveryOutcome
{
    NotStarted,
    Applied,
    GuardRejected,
    Failed,
    Uncertain,
    Blocked,
    Cancelled,
}

public sealed record MigrationRecoveryResult(
    MigrationRecoveryOutcome Outcome,
    RollbackExecutionResult? Execution = null,
    int AppliedActions = 0,
    int GuardRejectedActions = 0,
    int FailedActions = 0,
    int UncertainActions = 0)
{
    public bool IsRecovered => Outcome == MigrationRecoveryOutcome.Applied;
}
