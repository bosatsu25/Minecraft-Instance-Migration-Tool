using MinecraftInstanceMigration.App.Presentation;
using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Application.Reporting;
using MinecraftInstanceMigration.Application.Workflow;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.App.Tests;

public sealed class MigrationReportViewModelTests
{
    [Fact]
    public void CompletedEvidencePublishesReadablePathFreeReport()
    {
        var model = new MigrationReportViewModel(new MigrationReportProjector());

        model.Publish(Evidence(
            MigrationWorkflowState.Completed,
            execution: new ExecutionOrchestrationResult(
                ExecutionOrchestrationStatus.Completed,
                Journal: new ExecutionJournalReference("id", @"C:\private\journal.jsonl"),
                AppliedSteps: 1),
            backup: new BackupExecutionResult(BackupExecutionStatus.NotRequired)));

        Assert.True(model.HasReport);
        Assert.False(model.HasRecovery);
        Assert.Equal("Completed", model.OverallOutcome);
        Assert.Contains("Copy: 1", model.MigrationSummary, StringComparison.Ordinal);
        Assert.Contains("verification: Succeeded", model.ExecutionSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("private", Combined(model), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", Combined(model), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(MigrationRecoveryOutcome.Applied, MigrationReportOverallOutcome.Recovered)]
    [InlineData(MigrationRecoveryOutcome.GuardRejected, MigrationReportOverallOutcome.GuardRejected)]
    [InlineData(MigrationRecoveryOutcome.Failed, MigrationReportOverallOutcome.Failed)]
    [InlineData(MigrationRecoveryOutcome.Uncertain, MigrationReportOverallOutcome.Uncertain)]
    public void RecoveryOutcomesRemainDistinct(
        MigrationRecoveryOutcome rollbackOutcome,
        MigrationReportOverallOutcome expected)
    {
        var model = new MigrationReportViewModel(new MigrationReportProjector());
        MigrationRecoveryResult rollback = rollbackOutcome switch
        {
            MigrationRecoveryOutcome.Applied => new MigrationRecoveryResult(rollbackOutcome, AppliedActions: 1),
            MigrationRecoveryOutcome.GuardRejected => new MigrationRecoveryResult(rollbackOutcome, GuardRejectedActions: 1),
            MigrationRecoveryOutcome.Failed => new MigrationRecoveryResult(rollbackOutcome, FailedActions: 1),
            _ => new MigrationRecoveryResult(rollbackOutcome, UncertainActions: 1),
        };

        model.Publish(Evidence(
            MigrationWorkflowState.RecoveryRequired,
            execution: new ExecutionOrchestrationResult(ExecutionOrchestrationStatus.RecoveryRequired, AppliedSteps: 1),
            diagnosis: Diagnosis(),
            rollback: rollback));

        Assert.Equal(expected.ToString(), model.OverallOutcome);
        Assert.True(model.HasRecovery);
        Assert.Contains(rollbackOutcome.ToString(), model.RecoverySummary, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectorFailureDoesNotExposeExceptionOrInventReport()
    {
        var model = new MigrationReportViewModel(new ThrowingProjector());

        model.Publish(Evidence(MigrationWorkflowState.Completed));

        Assert.False(model.HasReport);
        Assert.Equal("Not available", model.OverallOutcome);
        Assert.Contains("generation failed", model.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private", model.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReportViewModelExposesNoWorkflowCommands()
    {
        Assert.DoesNotContain(
            typeof(MigrationReportViewModel).GetProperties(),
            property => property.PropertyType == typeof(RelayCommand));
    }

    private static string Combined(MigrationReportViewModel model) =>
        string.Join('|', model.Status, model.MigrationSummary, model.ExecutionSummary, model.BackupSummary, model.RecoverySummary);

    private static MigrationReportEvidence Evidence(
        MigrationWorkflowState state,
        BackupExecutionResult? backup = null,
        ExecutionOrchestrationResult? execution = null,
        MigrationRecoveryDiagnosis? diagnosis = null,
        MigrationRecoveryResult? rollback = null) =>
        new(
            state,
            new MigrationPreview(
                MigrationPlanStatus.Ready,
                [new MigrationPreviewEntry(
                    "config", true, EntryState.Directory, EntryState.Missing,
                    MigrationPreviewAction.Copy, MigrationPlanDisposition.ReadyToCopy, false)],
                [],
                []),
            new BackupPlan(BackupPlanStatus.NotRequired, [], []),
            backup ?? new BackupExecutionResult(BackupExecutionStatus.NotRequired),
            execution,
            diagnosis,
            rollback);

    private static MigrationRecoveryDiagnosis Diagnosis() =>
        new(
            MigrationRecoveryDiagnosisStatus.RollbackAvailable,
            AppliedExecutionSteps: 1,
            DeleteCreatedEntryCount: 1);

    private sealed class ThrowingProjector : IMigrationReportProjector
    {
        public MigrationReportCreationResult Create(MigrationReportEvidence evidence) =>
            throw new InvalidOperationException("private filesystem path");
    }
}
