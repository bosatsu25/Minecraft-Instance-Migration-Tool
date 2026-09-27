using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Application.Reporting;
using MinecraftInstanceMigration.Application.Workflow;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Tests;

public sealed class MigrationReportProjectorTests
{
    private readonly MigrationReportProjector projector = new();

    [Fact]
    public void CompletedEvidenceProducesCompletedReportWithExactCounts()
    {
        MigrationReportCreationResult result = projector.Create(Evidence(
            MigrationWorkflowState.Completed,
            execution: new ExecutionOrchestrationResult(
                ExecutionOrchestrationStatus.Completed,
                AppliedSteps: 2)));

        MigrationReport report = AssertCreated(result);
        Assert.Equal(MigrationReportOverallOutcome.Completed, report.OverallOutcome);
        Assert.Equal(3, report.Migration.SelectedEntryCount);
        Assert.Equal(1, report.Migration.CopyCount);
        Assert.Equal(1, report.Migration.ReplaceCount);
        Assert.Equal(1, report.Migration.SkipCount);
        Assert.Equal(1, report.Migration.ExcludedCount);
        Assert.Equal(1, report.Migration.SourceMissingCount);
        Assert.Equal(1, report.Migration.BlockedCount);
        Assert.Equal(2, report.Execution.PlannedWriteCount);
        Assert.Equal(2, report.Execution.AppliedCount);
        Assert.Equal(MigrationReportVerificationOutcome.Succeeded, report.Execution.VerificationOutcome);
        Assert.Null(report.Recovery);
    }

    [Theory]
    [InlineData(MigrationWorkflowState.Cancelled, MigrationReportOverallOutcome.Cancelled)]
    [InlineData(MigrationWorkflowState.Blocked, MigrationReportOverallOutcome.Blocked)]
    public void NonRecoveryTerminalStateUsesTypedWorkflowOutcome(
        MigrationWorkflowState state,
        MigrationReportOverallOutcome expected)
    {
        MigrationReport report = AssertCreated(projector.Create(Evidence(state)));

        Assert.Equal(expected, report.OverallOutcome);
        Assert.Equal(MigrationReportVerificationOutcome.NotRun, report.Execution.VerificationOutcome);
        Assert.Null(report.Recovery);
    }

    [Fact]
    public void RecoveryRequiredIncludesDiagnosisWithoutInventingRollbackResult()
    {
        MigrationRecoveryDiagnosis diagnosis = Diagnosis();

        MigrationReport report = AssertCreated(projector.Create(Evidence(
            MigrationWorkflowState.RecoveryRequired,
            execution: RecoveryExecution(),
            diagnosis: diagnosis)));

        Assert.Equal(MigrationReportOverallOutcome.RecoveryRequired, report.OverallOutcome);
        Assert.NotNull(report.Recovery);
        Assert.Equal(MigrationRecoveryDiagnosisStatus.RollbackAvailable, report.Recovery.DiagnosisStatus);
        Assert.Equal(1, report.Recovery.RollbackCandidateCount);
        Assert.Null(report.Recovery.RollbackOutcome);
    }

    [Fact]
    public void PartiallyAppliedCancellationRemainsRecoveryRequired()
    {
        MigrationReport report = AssertCreated(projector.Create(Evidence(
            MigrationWorkflowState.RecoveryRequired,
            execution: new ExecutionOrchestrationResult(
                ExecutionOrchestrationStatus.Cancelled,
                AppliedSteps: 1),
            diagnosis: Diagnosis())));

        Assert.Equal(MigrationReportOverallOutcome.RecoveryRequired, report.OverallOutcome);
        Assert.Equal(MigrationReportVerificationOutcome.NotRun, report.Execution.VerificationOutcome);
    }

    [Theory]
    [InlineData(MigrationRecoveryOutcome.Applied, MigrationReportOverallOutcome.Recovered)]
    [InlineData(MigrationRecoveryOutcome.GuardRejected, MigrationReportOverallOutcome.GuardRejected)]
    [InlineData(MigrationRecoveryOutcome.Failed, MigrationReportOverallOutcome.Failed)]
    [InlineData(MigrationRecoveryOutcome.Uncertain, MigrationReportOverallOutcome.Uncertain)]
    public void RollbackEvidenceControlsOverallOutcome(
        MigrationRecoveryOutcome rollbackOutcome,
        MigrationReportOverallOutcome expected)
    {
        MigrationRecoveryResult rollback = rollbackOutcome switch
        {
            MigrationRecoveryOutcome.Applied => new MigrationRecoveryResult(rollbackOutcome, AppliedActions: 1),
            MigrationRecoveryOutcome.GuardRejected => new MigrationRecoveryResult(rollbackOutcome, GuardRejectedActions: 1),
            MigrationRecoveryOutcome.Failed => new MigrationRecoveryResult(rollbackOutcome, FailedActions: 1),
            _ => new MigrationRecoveryResult(rollbackOutcome, UncertainActions: 1),
        };

        MigrationReport report = AssertCreated(projector.Create(Evidence(
            MigrationWorkflowState.RecoveryRequired,
            execution: RecoveryExecution(),
            diagnosis: Diagnosis(),
            rollback: rollback)));

        Assert.Equal(expected, report.OverallOutcome);
        Assert.Equal(rollbackOutcome, report.Recovery?.RollbackOutcome);
    }

    [Theory]
    [InlineData(MigrationRecoveryOutcome.Blocked, MigrationReportOverallOutcome.Blocked)]
    [InlineData(MigrationRecoveryOutcome.Cancelled, MigrationReportOverallOutcome.Cancelled)]
    public void NonMutatingRollbackOutcomeRemainsDistinct(
        MigrationRecoveryOutcome rollbackOutcome,
        MigrationReportOverallOutcome expected)
    {
        MigrationReport report = AssertCreated(projector.Create(Evidence(
            MigrationWorkflowState.RecoveryRequired,
            execution: RecoveryExecution(),
            diagnosis: Diagnosis(),
            rollback: new MigrationRecoveryResult(rollbackOutcome))));

        Assert.Equal(expected, report.OverallOutcome);
        Assert.Equal(rollbackOutcome, report.Recovery?.RollbackOutcome);
        Assert.Null(report.Recovery?.AppliedCount);
    }

    [Fact]
    public void PartialAppliedAndGuardRejectedIsNeverRecovered()
    {
        MigrationRecoveryDiagnosis diagnosis = Diagnosis(twoCandidates: true);
        var rollback = new MigrationRecoveryResult(
            MigrationRecoveryOutcome.GuardRejected,
            AppliedActions: 1,
            GuardRejectedActions: 1);

        MigrationReport report = AssertCreated(projector.Create(Evidence(
            MigrationWorkflowState.RecoveryRequired,
            execution: RecoveryExecution(applied: 2),
            diagnosis: diagnosis,
            rollback: rollback)));

        Assert.Equal(MigrationReportOverallOutcome.GuardRejected, report.OverallOutcome);
        Assert.Equal(1, report.Recovery?.AppliedCount);
        Assert.Equal(1, report.Recovery?.GuardRejectedCount);
    }

    [Fact]
    public void RecoveryResultWithoutDiagnosisFailsClosed()
    {
        MigrationReportCreationResult result = projector.Create(Evidence(
            MigrationWorkflowState.RecoveryRequired,
            execution: RecoveryExecution(),
            rollback: new MigrationRecoveryResult(MigrationRecoveryOutcome.Uncertain, UncertainActions: 1)));

        Assert.Equal(MigrationReportCreationStatus.InconsistentEvidence, result.Status);
        Assert.Null(result.Report);
    }

    [Fact]
    public void RecoveredRequiresEveryCandidateToHaveAppliedEvidence()
    {
        MigrationReportCreationResult result = projector.Create(Evidence(
            MigrationWorkflowState.RecoveryRequired,
            execution: RecoveryExecution(applied: 2),
            diagnosis: Diagnosis(twoCandidates: true),
            rollback: new MigrationRecoveryResult(MigrationRecoveryOutcome.Applied, AppliedActions: 1)));

        Assert.Equal(MigrationReportCreationStatus.InconsistentEvidence, result.Status);
        Assert.Null(result.Report);
    }

    [Fact]
    public void CompletedRequiresAppliedCountToMatchPlannedWrites()
    {
        MigrationReportCreationResult result = projector.Create(Evidence(
            MigrationWorkflowState.Completed,
            execution: new ExecutionOrchestrationResult(
                ExecutionOrchestrationStatus.Completed,
                AppliedSteps: 1)));

        Assert.Equal(MigrationReportCreationStatus.InconsistentEvidence, result.Status);
    }

    [Fact]
    public void CompletedWritesCannotUseNotRequiredExecutionStatus()
    {
        MigrationReportCreationResult result = projector.Create(Evidence(
            MigrationWorkflowState.Completed,
            execution: new ExecutionOrchestrationResult(
                ExecutionOrchestrationStatus.NotRequired,
                AppliedSteps: 2)));

        Assert.Equal(MigrationReportCreationStatus.InconsistentEvidence, result.Status);
    }

    [Fact]
    public void BackupSummaryUsesTypedBackupEvidence()
    {
        MigrationReport report = AssertCreated(projector.Create(Evidence(
            MigrationWorkflowState.Completed,
            backup: new BackupExecutionResult(BackupExecutionStatus.Completed, EntriesCopied: 1),
            execution: new ExecutionOrchestrationResult(ExecutionOrchestrationStatus.Completed, AppliedSteps: 2))));

        Assert.Equal(MigrationReportBackupOutcome.Completed, report.Backup.Outcome);
        Assert.Equal(1, report.Backup.ReplaceEntryCount);
    }

    [Fact]
    public void BackupFailureRemainsIndependentFailedReport()
    {
        MigrationReport report = AssertCreated(projector.Create(Evidence(
            MigrationWorkflowState.ReadyForBackup,
            backup: new BackupExecutionResult(BackupExecutionStatus.Failed),
            workflowFailure: MigrationWorkflowFailureKind.BackupFailed)));

        Assert.Equal(MigrationReportOverallOutcome.Failed, report.OverallOutcome);
        Assert.Equal(MigrationReportBackupOutcome.Failed, report.Backup.Outcome);
        Assert.Equal(MigrationReportVerificationOutcome.NotRun, report.Execution.VerificationOutcome);
    }

    [Fact]
    public void BackupFailureWithoutTypedBackupResultFailsClosed()
    {
        MigrationReportCreationResult result = projector.Create(Evidence(
            MigrationWorkflowState.ReadyForBackup,
            backup: null,
            workflowFailure: MigrationWorkflowFailureKind.BackupFailed,
            useDefaultBackup: false));

        Assert.Equal(MigrationReportCreationStatus.InconsistentEvidence, result.Status);
    }

    [Fact]
    public void PreExecutionOrchestratorFailureDoesNotInventVerificationFailure()
    {
        MigrationReport report = AssertCreated(projector.Create(Evidence(
            MigrationWorkflowState.Blocked,
            execution: new ExecutionOrchestrationResult(ExecutionOrchestrationStatus.Failed))));

        Assert.Equal(MigrationReportOverallOutcome.Blocked, report.OverallOutcome);
        Assert.Equal(MigrationReportVerificationOutcome.NotRun, report.Execution.VerificationOutcome);
    }

    [Fact]
    public void BackupRevalidationFailureIsReportedAsInvalid()
    {
        MigrationReport report = AssertCreated(projector.Create(Evidence(
            MigrationWorkflowState.Blocked,
            execution: new ExecutionOrchestrationResult(
                ExecutionOrchestrationStatus.Blocked,
                ExecutionOrchestrationFailureKind.BackupInvalid))));

        Assert.Equal(MigrationReportOverallOutcome.Blocked, report.OverallOutcome);
        Assert.Equal(MigrationReportBackupOutcome.Invalid, report.Backup.Outcome);
        Assert.Equal(MigrationReportVerificationOutcome.NotRun, report.Execution.VerificationOutcome);
    }

    [Theory]
    [InlineData(ExecutionOrchestrationFailureKind.MutationFailed, MigrationReportVerificationOutcome.NotRun)]
    [InlineData(ExecutionOrchestrationFailureKind.PostWriteVerificationFailed, MigrationReportVerificationOutcome.Failed)]
    public void VerificationOutcomeUsesTypedFailureEvidence(
        ExecutionOrchestrationFailureKind failureKind,
        MigrationReportVerificationOutcome expected)
    {
        MigrationRecoveryDiagnosis diagnosis = new(
            MigrationRecoveryDiagnosisStatus.ManualRecoveryRequired,
            FailedExecutionSteps: 1,
            NotStartedExecutionSteps: 1);
        MigrationReport report = AssertCreated(projector.Create(Evidence(
            MigrationWorkflowState.RecoveryRequired,
            execution: new ExecutionOrchestrationResult(
                ExecutionOrchestrationStatus.RecoveryRequired,
                failureKind),
            diagnosis: diagnosis)));

        Assert.Equal(expected, report.Execution.VerificationOutcome);
    }

    [Fact]
    public void CompletedWithInvalidBackupEvidenceFailsClosed()
    {
        MigrationReportCreationResult result = projector.Create(Evidence(
            MigrationWorkflowState.Completed,
            backup: new BackupExecutionResult(BackupExecutionStatus.Failed),
            execution: new ExecutionOrchestrationResult(ExecutionOrchestrationStatus.Completed, AppliedSteps: 2)));

        Assert.Equal(MigrationReportCreationStatus.InconsistentEvidence, result.Status);
    }

    [Fact]
    public void CompletedWithIncompleteBackupCountFailsClosed()
    {
        MigrationReportCreationResult result = projector.Create(Evidence(
            MigrationWorkflowState.Completed,
            backup: new BackupExecutionResult(BackupExecutionStatus.Completed, EntriesCopied: 0),
            execution: new ExecutionOrchestrationResult(ExecutionOrchestrationStatus.Completed, AppliedSteps: 2)));

        Assert.Equal(MigrationReportCreationStatus.InconsistentEvidence, result.Status);
    }

    [Fact]
    public void ReportModelContainsNoFilesystemPaths()
    {
        MigrationReport report = AssertCreated(projector.Create(Evidence(
            MigrationWorkflowState.Completed,
            backup: new BackupExecutionResult(
                BackupExecutionStatus.Completed,
                @"C:\private\backup",
                EntriesCopied: 1),
            execution: new ExecutionOrchestrationResult(
                ExecutionOrchestrationStatus.Completed,
                Journal: new ExecutionJournalReference("id", @"C:\private\journal.jsonl"),
                AppliedSteps: 2))));

        string text = System.Text.Json.JsonSerializer.Serialize(report);
        Assert.DoesNotContain("C:\\", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private", text, StringComparison.OrdinalIgnoreCase);
    }

    private static MigrationReport AssertCreated(MigrationReportCreationResult result)
    {
        Assert.Equal(MigrationReportCreationStatus.Created, result.Status);
        return Assert.IsType<MigrationReport>(result.Report);
    }

    private static MigrationReportEvidence Evidence(
        MigrationWorkflowState state,
        BackupExecutionResult? backup = null,
        ExecutionOrchestrationResult? execution = null,
        MigrationRecoveryDiagnosis? diagnosis = null,
        MigrationRecoveryResult? rollback = null,
        MigrationWorkflowFailureKind? workflowFailure = null,
        bool useDefaultBackup = true) =>
        new(
            state,
            Preview(),
            new BackupPlan(
                BackupPlanStatus.Ready,
                [new BackupPlanEntry("config", ExpectedEntryKind.Directory, EntryState.Directory)],
                []),
            backup ?? (useDefaultBackup
                ? new BackupExecutionResult(BackupExecutionStatus.Completed, EntriesCopied: 1)
                : null),
            execution,
            diagnosis,
            rollback,
            workflowFailure);

    private static MigrationPreview Preview() =>
        new(
            MigrationPlanStatus.Ready,
            [
                Entry("config", true, MigrationPreviewAction.Copy, MigrationPlanDisposition.ReadyToCopy),
                Entry("options.txt", true, MigrationPreviewAction.Replace, MigrationPlanDisposition.ReadyToReplace),
                Entry("resourcepacks", true, MigrationPreviewAction.Skip, MigrationPlanDisposition.SkippedDestinationConflict),
                Entry("saves", false, MigrationPreviewAction.Excluded, MigrationPlanDisposition.ExcludedBySelection),
                Entry("screenshots", false, MigrationPreviewAction.NoSource, MigrationPlanDisposition.SourceMissing),
                Entry("shaderpacks", false, MigrationPreviewAction.Blocked, MigrationPlanDisposition.BlockedSourceObservation),
            ],
            [],
            []);

    private static MigrationPreviewEntry Entry(
        string name,
        bool selected,
        MigrationPreviewAction action,
        MigrationPlanDisposition disposition) =>
        new(name, selected, EntryState.Directory, EntryState.Directory, action, disposition,
            action == MigrationPreviewAction.Replace);

    private static MigrationRecoveryDiagnosis Diagnosis(bool twoCandidates = false)
    {
        int count = twoCandidates ? 2 : 1;
        return new MigrationRecoveryDiagnosis(
            MigrationRecoveryDiagnosisStatus.RollbackAvailable,
            AppliedExecutionSteps: count,
            NotStartedExecutionSteps: 2 - count,
            DeleteCreatedEntryCount: count);
    }

    private static ExecutionOrchestrationResult RecoveryExecution(int applied = 1) =>
        new(ExecutionOrchestrationStatus.RecoveryRequired, AppliedSteps: applied);
}
