using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Application.Workflow;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Reporting;

public sealed class MigrationReportProjector : IMigrationReportProjector
{
    public MigrationReportCreationResult Create(MigrationReportEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        if (evidence.Preview is null)
        {
            return Failure(MigrationReportCreationFailureKind.MissingPreview);
        }

        MigrationPreview preview = evidence.Preview;
        var migration = new MigrationReportMigrationSummary(
            preview.Entries.Count(entry => entry.Selected),
            preview.CopyCount,
            preview.ReplaceCount,
            preview.SkipCount,
            preview.Entries.Count(entry => entry.Action == MigrationPreviewAction.Excluded),
            preview.NoSourceCount,
            preview.BlockedCount);
        int plannedWrites = preview.CopyCount + preview.ReplaceCount;

        if (!TryCreateOverallOutcome(
            evidence,
            plannedWrites,
            out MigrationReportOverallOutcome overall,
            out MigrationReportCreationFailureKind? failure))
        {
            return Failure(failure!.Value);
        }

        int failedSteps = evidence.RecoveryDiagnosis?.FailedExecutionSteps ?? 0;
        int uncertainSteps = evidence.RecoveryDiagnosis?.UncertainExecutionSteps ?? 0;
        var execution = new MigrationReportExecutionSummary(
            plannedWrites,
            evidence.ExecutionResult?.AppliedSteps ?? 0,
            failedSteps,
            uncertainSteps,
            VerificationOutcome(evidence));

        var backup = new MigrationReportBackupSummary(
            evidence.ExecutionResult?.FailureKind == ExecutionOrchestrationFailureKind.BackupInvalid
                ? MigrationReportBackupOutcome.Invalid
                : evidence.BackupResult?.Status switch
                {
                    BackupExecutionStatus.NotRequired => MigrationReportBackupOutcome.NotRequired,
                    BackupExecutionStatus.Completed => MigrationReportBackupOutcome.Completed,
                    BackupExecutionStatus.Cancelled => MigrationReportBackupOutcome.Cancelled,
                    BackupExecutionStatus.Failed => MigrationReportBackupOutcome.Failed,
                    _ => MigrationReportBackupOutcome.NotStarted,
                },
            preview.ReplaceCount);

        int terminalRollbackCount = evidence.RecoveryResult is null
            ? 0
            : evidence.RecoveryResult.AppliedActions +
              evidence.RecoveryResult.GuardRejectedActions +
              evidence.RecoveryResult.FailedActions +
              evidence.RecoveryResult.UncertainActions;
        bool hasRollbackCounts = evidence.RecoveryResult is not null && terminalRollbackCount > 0;
        MigrationReportRecoverySummary? recovery = evidence.RecoveryDiagnosis is null
            ? null
            : new MigrationReportRecoverySummary(
                evidence.RecoveryDiagnosis.Status,
                evidence.RecoveryDiagnosis.RollbackCandidateCount,
                evidence.RecoveryDiagnosis.DeleteCreatedEntryCount,
                evidence.RecoveryDiagnosis.RestoreFromBackupCount,
                evidence.RecoveryResult?.Outcome,
                hasRollbackCounts ? evidence.RecoveryResult!.AppliedActions : null,
                hasRollbackCounts ? evidence.RecoveryResult!.GuardRejectedActions : null,
                hasRollbackCounts ? evidence.RecoveryResult!.FailedActions : null,
                hasRollbackCounts ? evidence.RecoveryResult!.UncertainActions : null);

        return new MigrationReportCreationResult(
            MigrationReportCreationStatus.Created,
            new MigrationReport(overall, migration, execution, backup, recovery));
    }

    private static bool TryCreateOverallOutcome(
        MigrationReportEvidence evidence,
        int plannedWrites,
        out MigrationReportOverallOutcome outcome,
        out MigrationReportCreationFailureKind? failure)
    {
        failure = null;
        outcome = default;

        if (evidence.RecoveryResult is not null && evidence.RecoveryDiagnosis is null)
        {
            failure = MigrationReportCreationFailureKind.MissingRecoveryEvidence;
            return false;
        }

        if (evidence.WorkflowState == MigrationWorkflowState.RecoveryRequired)
        {
            ExecutionOrchestrationResult? executionResult = evidence.ExecutionResult;
            bool hasRecoveryRequiredExecution = executionResult?.Status ==
                ExecutionOrchestrationStatus.RecoveryRequired;
            bool hasPartiallyAppliedCancellation = executionResult?.Status ==
                ExecutionOrchestrationStatus.Cancelled && executionResult.AppliedSteps > 0;
            if ((!hasRecoveryRequiredExecution && !hasPartiallyAppliedCancellation) ||
                evidence.RecoveryDiagnosis is null)
            {
                failure = MigrationReportCreationFailureKind.MissingRecoveryEvidence;
                return false;
            }

            int executionEvidenceCount = evidence.RecoveryDiagnosis.AppliedExecutionSteps +
                evidence.RecoveryDiagnosis.FailedExecutionSteps +
                evidence.RecoveryDiagnosis.UncertainExecutionSteps +
                evidence.RecoveryDiagnosis.NotStartedExecutionSteps;
            if (executionEvidenceCount != plannedWrites ||
                executionResult!.AppliedSteps != evidence.RecoveryDiagnosis.AppliedExecutionSteps)
            {
                failure = MigrationReportCreationFailureKind.InconsistentExecutionEvidence;
                return false;
            }

            if (evidence.RecoveryResult is null)
            {
                outcome = MigrationReportOverallOutcome.RecoveryRequired;
                return true;
            }

            if (!RecoveryEvidenceIsConsistent(evidence.RecoveryDiagnosis, evidence.RecoveryResult))
            {
                failure = MigrationReportCreationFailureKind.InconsistentRecoveryEvidence;
                return false;
            }

            outcome = evidence.RecoveryResult.Outcome switch
            {
                MigrationRecoveryOutcome.Applied => MigrationReportOverallOutcome.Recovered,
                MigrationRecoveryOutcome.GuardRejected => MigrationReportOverallOutcome.GuardRejected,
                MigrationRecoveryOutcome.Failed => MigrationReportOverallOutcome.Failed,
                MigrationRecoveryOutcome.Uncertain => MigrationReportOverallOutcome.Uncertain,
                MigrationRecoveryOutcome.Blocked => MigrationReportOverallOutcome.Blocked,
                MigrationRecoveryOutcome.Cancelled => MigrationReportOverallOutcome.Cancelled,
                _ => MigrationReportOverallOutcome.RecoveryRequired,
            };
            return true;
        }

        if (evidence.RecoveryDiagnosis is not null || evidence.RecoveryResult is not null)
        {
            failure = MigrationReportCreationFailureKind.InconsistentRecoveryEvidence;
            return false;
        }

        switch (evidence.WorkflowState)
        {
            case MigrationWorkflowState.Completed:
                if (evidence.ExecutionResult?.IsComplete != true)
                {
                    failure = MigrationReportCreationFailureKind.MissingExecutionEvidence;
                    return false;
                }

                if (evidence.ExecutionResult.AppliedSteps != plannedWrites)
                {
                    failure = MigrationReportCreationFailureKind.InconsistentExecutionEvidence;
                    return false;
                }

                if ((plannedWrites == 0 && evidence.ExecutionResult.Status != ExecutionOrchestrationStatus.NotRequired) ||
                    (plannedWrites > 0 && evidence.ExecutionResult.Status != ExecutionOrchestrationStatus.Completed))
                {
                    failure = MigrationReportCreationFailureKind.InconsistentExecutionEvidence;
                    return false;
                }

                if (!BackupEvidenceIsConsistent(evidence, plannedWrites))
                {
                    failure = MigrationReportCreationFailureKind.InconsistentExecutionEvidence;
                    return false;
                }

                outcome = MigrationReportOverallOutcome.Completed;
                return true;

            case MigrationWorkflowState.Cancelled:
                outcome = MigrationReportOverallOutcome.Cancelled;
                return true;

            case MigrationWorkflowState.Blocked:
                outcome = MigrationReportOverallOutcome.Blocked;
                return true;

            case MigrationWorkflowState.ReadyForBackup when
                evidence.WorkflowFailure == MigrationWorkflowFailureKind.BackupFailed:
                if (evidence.BackupResult?.Status != BackupExecutionStatus.Failed)
                {
                    failure = MigrationReportCreationFailureKind.InconsistentExecutionEvidence;
                    return false;
                }

                outcome = MigrationReportOverallOutcome.Failed;
                return true;

            case MigrationWorkflowState.ReadyForBackup or MigrationWorkflowState.BackupReady when
                evidence.WorkflowFailure is not null:
                outcome = MigrationReportOverallOutcome.Blocked;
                return true;

            default:
                failure = MigrationReportCreationFailureKind.UnsupportedWorkflowState;
                return false;
        }
    }

    private static bool BackupEvidenceIsConsistent(
        MigrationReportEvidence evidence,
        int plannedWrites)
    {
        if (plannedWrites == 0)
        {
            return evidence.BackupResult?.IsComplete == true;
        }

        int replaceCount = evidence.Preview!.ReplaceCount;
        if (replaceCount == 0)
        {
            return evidence.BackupResult?.Status == BackupExecutionStatus.NotRequired;
        }

        return evidence.BackupResult?.Status == BackupExecutionStatus.Completed &&
            evidence.BackupResult.EntriesCopied == replaceCount &&
            evidence.BackupPlan?.Entries.Count == replaceCount;
    }

    private static bool RecoveryEvidenceIsConsistent(
        MigrationRecoveryDiagnosis diagnosis,
        MigrationRecoveryResult result)
    {
        int candidates = diagnosis.RollbackCandidateCount;
        int terminal = result.AppliedActions +
            result.GuardRejectedActions +
            result.FailedActions +
            result.UncertainActions;

        if (terminal > candidates)
        {
            return false;
        }

        return result.Outcome switch
        {
            MigrationRecoveryOutcome.Applied =>
                candidates > 0 &&
                result.AppliedActions == candidates &&
                terminal == result.AppliedActions,
            MigrationRecoveryOutcome.GuardRejected => result.GuardRejectedActions > 0,
            MigrationRecoveryOutcome.Failed => result.FailedActions > 0,
            MigrationRecoveryOutcome.Uncertain => true,
            _ => true,
        };
    }

    private static MigrationReportVerificationOutcome VerificationOutcome(
        MigrationReportEvidence evidence) =>
        evidence.ExecutionResult?.Status switch
        {
            ExecutionOrchestrationStatus.Completed or ExecutionOrchestrationStatus.NotRequired =>
                MigrationReportVerificationOutcome.Succeeded,
            ExecutionOrchestrationStatus.RecoveryRequired when evidence.RecoveryDiagnosis?.UncertainExecutionSteps > 0 =>
                MigrationReportVerificationOutcome.Uncertain,
            ExecutionOrchestrationStatus.RecoveryRequired when evidence.ExecutionResult.FailureKind ==
                ExecutionOrchestrationFailureKind.PostWriteVerificationFailed =>
                MigrationReportVerificationOutcome.Failed,
            _ => MigrationReportVerificationOutcome.NotRun,
        };

    private static MigrationReportCreationResult Failure(MigrationReportCreationFailureKind kind) =>
        new(MigrationReportCreationStatus.InconsistentEvidence, FailureKind: kind);
}
