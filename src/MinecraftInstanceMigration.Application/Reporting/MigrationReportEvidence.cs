using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Application.Workflow;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Reporting;

public sealed record MigrationReportEvidence(
    MigrationWorkflowState WorkflowState,
    MigrationPreview? Preview,
    BackupPlan? BackupPlan,
    BackupExecutionResult? BackupResult,
    ExecutionOrchestrationResult? ExecutionResult,
    MigrationRecoveryDiagnosis? RecoveryDiagnosis,
    MigrationRecoveryResult? RecoveryResult,
    MigrationWorkflowFailureKind? WorkflowFailure = null)
{
    public static MigrationReportEvidence FromSession(
        MigrationWorkflowSession session,
        MigrationRecoveryDiagnosis? recoveryDiagnosis = null,
        MigrationRecoveryResult? recoveryResult = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new MigrationReportEvidence(
            session.State,
            session.MigrationPreview,
            session.BackupPlan,
            session.BackupResult,
            session.ExecutionResult,
            recoveryDiagnosis,
            recoveryResult,
            session.FailureKind);
    }
}
