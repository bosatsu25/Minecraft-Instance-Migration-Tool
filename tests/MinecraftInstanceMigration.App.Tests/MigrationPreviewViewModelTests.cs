using MinecraftInstanceMigration.App.Presentation;
using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Application.Inspection;
using MinecraftInstanceMigration.Application.Planning;
using MinecraftInstanceMigration.Application.Reporting;
using MinecraftInstanceMigration.Application.Workflow;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.App.Tests;

public sealed class MigrationPreviewViewModelTests
{
    [Fact]
    public async Task ReadyPreviewCanExecuteAfterExplicitConfirmation()
    {
        var calls = new List<string>();
        var confirmation = new StubConfirmation(confirmed: true);
        var report = new MigrationReportViewModel(new MigrationReportProjector());
        var model = CreateExecutableModel(calls, confirmation, reportViewModel: report);
        model.SourcePath = "source";
        model.DestinationPath = "destination";
        model.SafetyWorkspacePath = "workspace";

        await model.GeneratePreviewAsync();

        Assert.True(model.ExecuteMigrationCommand.CanExecute(null));
        await model.ExecuteMigrationAsync();

        Assert.Equal(MigrationWorkflowState.Completed, model.WorkflowState);
        Assert.Equal(["execute"], calls);
        MigrationExecutionConfirmation request = Assert.Single(confirmation.Requests);
        Assert.Equal("source", request.SourceRoot);
        Assert.Equal("destination", request.DestinationRoot);
        Assert.Equal("workspace", request.SafetyWorkspace);
        Assert.Equal(1, request.CopyCount);
        Assert.Equal(0, request.ReplaceCount);
        Assert.Equal(0, request.SkipCount);
        Assert.False(request.BackupRequired);
        Assert.Equal("Completed", report.OverallOutcome);
        Assert.Contains("Copy: 1", report.MigrationSummary, StringComparison.Ordinal);
        Assert.Contains("verification: Succeeded", report.ExecutionSummary, StringComparison.Ordinal);

        await model.GeneratePreviewAsync();

        Assert.False(report.HasReport);
        Assert.Equal("Not available", report.OverallOutcome);
    }

    [Fact]
    public async Task ReportProjectionFailureDoesNotChangeCompletedWorkflow()
    {
        var report = new MigrationReportViewModel(new ThrowingReportProjector());
        var model = CreateExecutableModel(
            [],
            new StubConfirmation(confirmed: true),
            reportViewModel: report);
        model.SourcePath = "source";
        model.DestinationPath = "destination";
        model.SafetyWorkspacePath = "workspace";
        await model.GeneratePreviewAsync();

        await model.ExecuteMigrationAsync();

        Assert.Equal(MigrationWorkflowState.Completed, model.WorkflowState);
        Assert.False(report.HasReport);
        Assert.Contains("generation failed", report.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfirmationRejectionDoesNotCallBackupOrExecution()
    {
        var calls = new List<string>();
        var model = CreateExecutableModel(calls, new StubConfirmation(confirmed: false));
        model.SourcePath = "source";
        model.DestinationPath = "destination";
        model.SafetyWorkspacePath = "workspace";
        await model.GeneratePreviewAsync();

        await model.ExecuteMigrationAsync();

        Assert.Empty(calls);
        Assert.Equal(MigrationWorkflowState.ReadyForBackup, model.WorkflowState);
    }

    [Fact]
    public async Task PendingChoicesDisableExecution()
    {
        var model = CreateExecutableModel([], new StubConfirmation(confirmed: true));
        model.SourcePath = "source";
        model.DestinationPath = "destination";
        model.SafetyWorkspacePath = "workspace";
        await model.GeneratePreviewAsync();
        model.SelectedEntry = Entry(model, "config");

        model.ExcludeSelectedCommand.Execute(null);

        Assert.False(model.ExecuteMigrationCommand.CanExecute(null));
    }

    [Fact]
    public async Task NeedsDecisionAndBlockedPreviewsCannotExecute()
    {
        var needsDecision = CreateExecutableModel(
            [],
            new StubConfirmation(confirmed: true),
            destinationConflict: true);
        needsDecision.SourcePath = "source";
        needsDecision.DestinationPath = "destination";
        needsDecision.SafetyWorkspacePath = "workspace";
        await needsDecision.GeneratePreviewAsync();

        var blocked = CreateExecutableModel(
            [],
            new StubConfirmation(confirmed: true),
            inspector: new StubInspector((path, _) => Task.FromResult(
                path == "source"
                    ? Inspection(("config", EntryState.ReparsePoint))
                    : Inspection())));
        blocked.SourcePath = "source";
        blocked.DestinationPath = "destination";
        blocked.SafetyWorkspacePath = "workspace";
        await blocked.GeneratePreviewAsync();

        Assert.Equal("NeedsDecision", needsDecision.PlanStatus);
        Assert.False(needsDecision.ExecuteMigrationCommand.CanExecute(null));
        Assert.Equal("Blocked", blocked.PlanStatus);
        Assert.False(blocked.ExecuteMigrationCommand.CanExecute(null));
    }

    [Fact]
    public async Task ReplaceRunsBackupBeforeExecution()
    {
        var calls = new List<string>();
        var model = CreateExecutableModel(
            calls,
            new StubConfirmation(confirmed: true),
            destinationConflict: true,
            backupResult: new BackupExecutionResult(
                BackupExecutionStatus.Completed,
                "backup-root"));
        model.SourcePath = "source";
        model.DestinationPath = "destination";
        model.SafetyWorkspacePath = "workspace";
        await model.GeneratePreviewAsync();
        model.SelectedEntry = Entry(model, "config");
        model.ReplaceConflictCommand.Execute(null);
        model.ApplyChoicesCommand.Execute(null);

        await model.ExecuteMigrationAsync();

        Assert.Equal(["backup", "execute"], calls);
        Assert.Equal(MigrationWorkflowState.Completed, model.WorkflowState);
    }

    [Theory]
    [InlineData(BackupExecutionStatus.Failed, MigrationWorkflowState.ReadyForBackup, "BackupFailed", "Failed")]
    [InlineData(BackupExecutionStatus.Cancelled, MigrationWorkflowState.Cancelled, "Cancelled", "Cancelled")]
    public async Task BackupFailureOrCancellationNeverStartsExecution(
        BackupExecutionStatus backupStatus,
        MigrationWorkflowState expectedState,
        string expectedStatus,
        string expectedReportOutcome)
    {
        var calls = new List<string>();
        var report = new MigrationReportViewModel(new MigrationReportProjector());
        var model = CreateExecutableModel(
            calls,
            new StubConfirmation(confirmed: true),
            destinationConflict: true,
            backupResult: new BackupExecutionResult(backupStatus),
            reportViewModel: report);
        model.SourcePath = "source";
        model.DestinationPath = "destination";
        model.SafetyWorkspacePath = "workspace";
        await model.GeneratePreviewAsync();
        model.SelectedEntry = Entry(model, "config");
        model.ReplaceConflictCommand.Execute(null);
        model.ApplyChoicesCommand.Execute(null);

        await model.ExecuteMigrationAsync();

        Assert.Equal(["backup"], calls);
        Assert.Equal(expectedState, model.WorkflowState);
        Assert.Contains(expectedStatus, model.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No files were changed", model.Status, StringComparison.Ordinal);
        Assert.Equal(expectedReportOutcome, report.OverallOutcome);
    }

    [Fact]
    public async Task BackupPreparationFailurePublishesBlockedReportWithoutStartingIo()
    {
        var calls = new List<string>();
        var report = new MigrationReportViewModel(new MigrationReportProjector());
        var model = CreateExecutableModel(
            calls,
            new StubConfirmation(confirmed: true),
            backupPlanner: new ThrowingBackupPlanner(),
            reportViewModel: report);
        model.SourcePath = "source";
        model.DestinationPath = "destination";
        model.SafetyWorkspacePath = "workspace";
        await model.GeneratePreviewAsync();

        await model.ExecuteMigrationAsync();

        Assert.Empty(calls);
        Assert.Equal(MigrationWorkflowState.ReadyForBackup, model.WorkflowState);
        Assert.Equal("Blocked", report.OverallOutcome);
        Assert.Contains("BackupNotReady", model.Status, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ExecutionOrchestrationStatus.Failed, MigrationWorkflowState.Blocked, "blocked")]
    [InlineData(ExecutionOrchestrationStatus.RecoveryRequired, MigrationWorkflowState.RecoveryRequired, "RecoveryRequired")]
    public async Task ExecutionFailureUsesTypedWorkflowState(
        ExecutionOrchestrationStatus executionStatus,
        MigrationWorkflowState expectedState,
        string expectedMessage)
    {
        var model = CreateExecutableModel(
            [],
            new StubConfirmation(confirmed: true),
            execution: new RecordingExecutionOrchestrator([], executionStatus));
        model.SourcePath = "source";
        model.DestinationPath = "destination";
        model.SafetyWorkspacePath = "workspace";
        await model.GeneratePreviewAsync();

        await model.ExecuteMigrationAsync();

        Assert.Equal(expectedState, model.WorkflowState);
        Assert.Contains(expectedMessage, model.Status, StringComparison.OrdinalIgnoreCase);
        Assert.False(model.ExecuteMigrationCommand.CanExecute(null));
    }

    [Fact]
    public async Task ExecutionCannotStartTwiceOrChangePathsWhileRunning()
    {
        var pending = new TaskCompletionSource<ExecutionOrchestrationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var execution = new PendingExecutionOrchestrator(pending.Task);
        var model = CreateExecutableModel(
            [],
            new StubConfirmation(confirmed: true),
            execution: execution);
        model.SourcePath = "source";
        model.DestinationPath = "destination";
        model.SafetyWorkspacePath = "workspace";
        await model.GeneratePreviewAsync();

        Task first = model.ExecuteMigrationAsync();
        Assert.True(model.IsBusy);
        Assert.False(model.ExecuteMigrationCommand.CanExecute(null));
        Assert.Contains("Executing", model.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("verifying", model.Status, StringComparison.OrdinalIgnoreCase);
        string originalDestination = model.DestinationPath;
        model.DestinationPath = "changed";
        Task second = model.ExecuteMigrationAsync();

        Assert.Equal(originalDestination, model.DestinationPath);
        Assert.Equal(1, execution.Calls);
        Assert.True(second.IsCompletedSuccessfully);

        pending.SetResult(new ExecutionOrchestrationResult(
            ExecutionOrchestrationStatus.Completed,
            AppliedSteps: 1));
        await first;
    }

    [Fact]
    public async Task PathChangeInvalidatesExecutionSession()
    {
        var model = CreateExecutableModel([], new StubConfirmation(confirmed: true));
        model.SourcePath = "source";
        model.DestinationPath = "destination";
        model.SafetyWorkspacePath = "workspace";
        await model.GeneratePreviewAsync();
        Assert.True(model.ExecuteMigrationCommand.CanExecute(null));

        model.SourcePath = "new-source";

        Assert.False(model.ExecuteMigrationCommand.CanExecute(null));
        Assert.Equal(MigrationWorkflowState.SelectRoots, model.WorkflowState);
    }

    [Fact]
    public async Task ExecutionExceptionDoesNotExposePrivateMessage()
    {
        var model = CreateExecutableModel(
            [],
            new StubConfirmation(confirmed: true),
            execution: new ThrowingPrivateExecutionOrchestrator());
        model.SourcePath = "source";
        model.DestinationPath = "destination";
        model.SafetyWorkspacePath = "workspace";
        await model.GeneratePreviewAsync();

        await model.ExecuteMigrationAsync();

        Assert.Equal(MigrationWorkflowState.RecoveryRequired, model.WorkflowState);
        Assert.DoesNotContain("private", model.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RecoveryRequired", model.Status);
    }

    [Fact]
    public async Task RecoveryRequiredLocksInputsAndChoices()
    {
        var model = CreateExecutableModel(
            [],
            new StubConfirmation(confirmed: true),
            execution: new RecordingExecutionOrchestrator(
                [],
                ExecutionOrchestrationStatus.RecoveryRequired));
        model.SourcePath = "source";
        model.DestinationPath = "destination";
        model.SafetyWorkspacePath = "workspace";
        await model.GeneratePreviewAsync();
        model.SelectedEntry = Entry(model, "config");

        await model.ExecuteMigrationAsync();
        model.SourcePath = "new-source";
        model.SafetyWorkspacePath = "new-workspace";

        Assert.Equal(MigrationWorkflowState.RecoveryRequired, model.WorkflowState);
        Assert.Equal("source", model.SourcePath);
        Assert.Equal("workspace", model.SafetyWorkspacePath);
        Assert.False(model.CanEditPaths);
        Assert.False(model.BrowseSourceCommand.CanExecute(null));
        Assert.False(model.BrowseDestinationCommand.CanExecute(null));
        Assert.False(model.BrowseSafetyWorkspaceCommand.CanExecute(null));
        Assert.False(model.GeneratePreviewCommand.CanExecute(null));
        Assert.False(model.ExcludeSelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task RecoveryRequiredPublishesTypedDiagnosisAndEnablesRollback()
    {
        var recovery = new StubRecoveryCoordinator(RollbackableDiagnosis());
        var model = CreateExecutableModel(
            [],
            new StubConfirmation(confirmed: true),
            execution: new RecordingExecutionOrchestrator([], ExecutionOrchestrationStatus.RecoveryRequired),
            recovery: recovery,
            rollbackConfirmation: new StubRollbackConfirmation(confirmed: true));
        model.SourcePath = "source";
        model.DestinationPath = "destination";
        model.SafetyWorkspacePath = "workspace";
        await model.GeneratePreviewAsync();

        await model.ExecuteMigrationAsync();

        Assert.True(model.IsRecoveryVisible);
        Assert.Equal("RollbackAvailable", model.RecoveryDiagnosisStatus);
        Assert.Contains("Applied: 1", model.RecoverySummary, StringComparison.Ordinal);
        Assert.Contains("candidates: 1", model.RecoverySummary, StringComparison.OrdinalIgnoreCase);
        Assert.True(model.RollbackMigrationCommand.CanExecute(null));
        Assert.Equal(1, recovery.DiagnoseCalls);
    }

    [Fact]
    public async Task NonRecoveryOrBlockedDiagnosisCannotRollback()
    {
        var completed = CreateExecutableModel([], new StubConfirmation(confirmed: true));
        completed.SourcePath = "source";
        completed.DestinationPath = "destination";
        completed.SafetyWorkspacePath = "workspace";
        await completed.GeneratePreviewAsync();
        await completed.ExecuteMigrationAsync();
        Assert.False(completed.IsRecoveryVisible);
        Assert.False(completed.RollbackMigrationCommand.CanExecute(null));

        var recovery = new StubRecoveryCoordinator(new MigrationRecoveryDiagnosis(
            MigrationRecoveryDiagnosisStatus.Blocked));
        var blocked = CreateExecutableModel(
            [], new StubConfirmation(true),
            execution: new RecordingExecutionOrchestrator([], ExecutionOrchestrationStatus.RecoveryRequired),
            recovery: recovery);
        blocked.SourcePath = "source";
        blocked.DestinationPath = "destination";
        blocked.SafetyWorkspacePath = "workspace";
        await blocked.GeneratePreviewAsync();
        await blocked.ExecuteMigrationAsync();
        Assert.Equal("Blocked", blocked.RecoveryDiagnosisStatus);
        Assert.False(blocked.RollbackMigrationCommand.CanExecute(null));
    }

    [Fact]
    public async Task RejectedRollbackConfirmationDoesNotCallRecoveryExecutor()
    {
        var recovery = new StubRecoveryCoordinator(RollbackableDiagnosis());
        var confirmation = new StubRollbackConfirmation(confirmed: false);
        var model = CreateExecutableModel(
            [], new StubConfirmation(true),
            execution: new RecordingExecutionOrchestrator([], ExecutionOrchestrationStatus.RecoveryRequired),
            recovery: recovery,
            rollbackConfirmation: confirmation);
        model.SourcePath = "source";
        model.DestinationPath = "destination";
        model.SafetyWorkspacePath = "workspace";
        await model.GeneratePreviewAsync();
        await model.ExecuteMigrationAsync();

        await model.RollbackMigrationAsync();

        Assert.Equal(0, recovery.ExecuteCalls);
        MigrationRollbackConfirmation request = Assert.Single(confirmation.Requests);
        Assert.Equal(1, request.RollbackCandidateCount);
        Assert.Equal(1, request.DeleteCreatedEntryCount);
        Assert.False(request.BackupRequired);
    }

    [Theory]
    [InlineData(MigrationRecoveryOutcome.Applied, "Recovered")]
    [InlineData(MigrationRecoveryOutcome.GuardRejected, "not modified")]
    [InlineData(MigrationRecoveryOutcome.Failed, "failed")]
    [InlineData(MigrationRecoveryOutcome.Uncertain, "cannot be proven")]
    public async Task RollbackOutcomeUsesDistinctSafePresentation(
        MigrationRecoveryOutcome outcome,
        string expected)
    {
        var recovery = new StubRecoveryCoordinator(
            RollbackableDiagnosis(),
            new MigrationRecoveryResult(outcome));
        var model = CreateExecutableModel(
            [], new StubConfirmation(true),
            execution: new RecordingExecutionOrchestrator([], ExecutionOrchestrationStatus.RecoveryRequired),
            recovery: recovery,
            rollbackConfirmation: new StubRollbackConfirmation(true));
        model.SourcePath = "source";
        model.DestinationPath = "destination";
        model.SafetyWorkspacePath = "workspace";
        await model.GeneratePreviewAsync();
        await model.ExecuteMigrationAsync();

        await model.RollbackMigrationAsync();

        Assert.Equal(outcome.ToString(), model.RollbackOutcome);
        Assert.Contains(expected, model.RecoveryStatus, StringComparison.OrdinalIgnoreCase);
        Assert.False(model.RollbackMigrationCommand.CanExecute(null));
        Assert.Equal(1, recovery.ExecuteCalls);
    }

    [Fact]
    public async Task RollbackLocksInputsAndPreventsDuplicateInvocation()
    {
        var pending = new TaskCompletionSource<MigrationRecoveryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovery = new PendingRecoveryCoordinator(RollbackableDiagnosis(), pending.Task);
        var model = CreateExecutableModel(
            [], new StubConfirmation(true),
            execution: new RecordingExecutionOrchestrator([], ExecutionOrchestrationStatus.RecoveryRequired),
            recovery: recovery,
            rollbackConfirmation: new StubRollbackConfirmation(true));
        model.SourcePath = "source";
        model.DestinationPath = "destination";
        model.SafetyWorkspacePath = "workspace";
        await model.GeneratePreviewAsync();
        await model.ExecuteMigrationAsync();

        Task first = model.RollbackMigrationAsync();
        Assert.True(model.IsBusy);
        Assert.False(model.RollbackMigrationCommand.CanExecute(null));
        string source = model.SourcePath;
        model.SourcePath = "changed";
        Task second = model.RollbackMigrationAsync();
        Assert.Equal(source, model.SourcePath);
        Assert.True(second.IsCompletedSuccessfully);
        Assert.Equal(1, recovery.ExecuteCalls);

        pending.SetResult(new MigrationRecoveryResult(MigrationRecoveryOutcome.Applied));
        await first;
        await model.RollbackMigrationAsync();
        Assert.Equal(1, recovery.ExecuteCalls);
    }

    [Fact]
    public async Task RecoveryFailuresDoNotExposePrivateDetails()
    {
        var recovery = new ThrowingRecoveryCoordinator();
        var model = CreateExecutableModel(
            [], new StubConfirmation(true),
            execution: new RecordingExecutionOrchestrator([], ExecutionOrchestrationStatus.RecoveryRequired),
            recovery: recovery);
        model.SourcePath = "source";
        model.DestinationPath = "destination";
        model.SafetyWorkspacePath = "workspace";
        await model.GeneratePreviewAsync();

        await model.ExecuteMigrationAsync();

        Assert.DoesNotContain("private", model.RecoveryStatus, StringComparison.OrdinalIgnoreCase);
        Assert.False(model.RollbackMigrationCommand.CanExecute(null));
    }

    [Fact]
    public async Task CancelDuringExecutionUsesWorkflowCancellationState()
    {
        var execution = new CancellableExecutionOrchestrator();
        var model = CreateExecutableModel(
            [],
            new StubConfirmation(confirmed: true),
            execution: execution);
        model.SourcePath = "source";
        model.DestinationPath = "destination";
        model.SafetyWorkspacePath = "workspace";
        await model.GeneratePreviewAsync();

        Task running = model.ExecuteMigrationAsync();
        await execution.Started.Task;
        model.CancelCommand.Execute(null);
        await running;

        Assert.Equal(MigrationWorkflowState.Cancelled, model.WorkflowState);
        Assert.Contains("cancelled", model.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GeneratesRecommendedDryRunFromTwoInspections()
    {
        var calls = new List<string>();
        var inspector = new StubInspector((path, _) =>
        {
            calls.Add(path);
            return Task.FromResult(path == "source"
                ? Inspection(("config", EntryState.Directory), ("saves", EntryState.Directory))
                : Inspection());
        });
        var model = CreateModel(inspector);
        model.SourcePath = "source";
        model.DestinationPath = "destination";

        await model.GeneratePreviewAsync();

        Assert.Equal(new[] { "source", "destination" }, calls);
        Assert.Equal("Ready", model.PlanStatus);
        Assert.Equal(MigrationPreviewAction.Copy, Entry(model, "config").Action);
        Assert.Equal(MigrationPreviewAction.Excluded, Entry(model, "saves").Action);
        Assert.False(Entry(model, "saves").Selected);
        Assert.False(Entry(model, "screenshots").Selected);
        Assert.Contains("No files were changed", model.Status);
        Assert.False(model.HasPendingChoices);
    }

    [Fact]
    public async Task CustomSelectionCanOptInWithoutReinspection()
    {
        var calls = new List<string>();
        var inspector = new StubInspector((path, _) =>
        {
            calls.Add(path);
            return Task.FromResult(path == "source"
                ? Inspection(("config", EntryState.Directory), ("saves", EntryState.Directory))
                : Inspection());
        });
        var model = CreateModel(inspector);
        model.SourcePath = "source";
        model.DestinationPath = "destination";
        await model.GeneratePreviewAsync();

        MigrationSelectionEntryViewModel saves = Entry(model, "saves");
        model.SelectedEntry = saves;
        Assert.True(model.IncludeSelectedCommand.CanExecute(null));
        model.IncludeSelectedCommand.Execute(null);
        Assert.True(model.HasPendingChoices);
        Assert.Contains("Included", model.SelectedChoiceSummary);
        Assert.True(model.ApplyChoicesCommand.CanExecute(null));

        model.ApplyChoicesCommand.Execute(null);

        Assert.Equal("Ready", model.PlanStatus);
        Assert.Equal(MigrationPreviewAction.Copy, Entry(model, "saves").Action);
        Assert.Equal(new[] { "source", "destination" }, calls);
        Assert.False(model.HasPendingChoices);
    }

    [Fact]
    public async Task ExistingDestinationCanBeReplacedExplicitly()
    {
        var inspector = new StubInspector((path, _) => Task.FromResult(
            path == "source"
                ? Inspection(("config", EntryState.Directory))
                : Inspection(("config", EntryState.Directory))));
        var model = CreateModel(inspector);
        model.SourcePath = "source";
        model.DestinationPath = "destination";

        await model.GeneratePreviewAsync();

        Assert.Equal("NeedsDecision", model.PlanStatus);
        MigrationSelectionEntryViewModel config = Entry(model, "config");
        model.SelectedEntry = config;
        Assert.True(model.ReplaceConflictCommand.CanExecute(null));
        model.ReplaceConflictCommand.Execute(null);
        model.ApplyChoicesCommand.Execute(null);

        Assert.Equal("Ready", model.PlanStatus);
        config = Entry(model, "config");
        Assert.Equal(MigrationPreviewAction.Replace, config.Action);
        Assert.Equal(MigrationPlanDisposition.ReadyToReplace, config.PlanDisposition);
        Assert.True(config.RequiresBackup);
        Assert.Equal("config: Included; conflict: Replace.", model.SelectedChoiceSummary);
    }

    [Fact]
    public async Task ExistingDestinationCanBeSkippedExplicitly()
    {
        var inspector = new StubInspector((path, _) => Task.FromResult(
            path == "source"
                ? Inspection(("config", EntryState.Directory))
                : Inspection(("config", EntryState.Directory))));
        var model = CreateModel(inspector);
        model.SourcePath = "source";
        model.DestinationPath = "destination";

        await model.GeneratePreviewAsync();

        MigrationSelectionEntryViewModel config = Entry(model, "config");
        model.SelectedEntry = config;
        Assert.True(model.SkipConflictCommand.CanExecute(null));
        model.SkipConflictCommand.Execute(null);
        model.ApplyChoicesCommand.Execute(null);

        Assert.Equal("Ready", model.PlanStatus);
        config = Entry(model, "config");
        Assert.Equal(MigrationPreviewAction.Skip, config.Action);
        Assert.False(config.RequiresBackup);
        Assert.Equal("config: Included; conflict: Skip.", model.SelectedChoiceSummary);
    }

    [Fact]
    public async Task ConflictDecisionCanBeClearedBeforeApply()
    {
        var calls = new List<string>();
        var inspector = new StubInspector((path, _) =>
        {
            calls.Add(path);
            return Task.FromResult(Inspection(("config", EntryState.Directory)));
        });
        var model = CreateModel(inspector);
        model.SourcePath = "source";
        model.DestinationPath = "destination";
        await model.GeneratePreviewAsync();

        model.SelectedEntry = Entry(model, "config");
        model.ReplaceConflictCommand.Execute(null);
        Assert.True(model.ClearConflictCommand.CanExecute(null));

        model.ClearConflictCommand.Execute(null);

        Assert.Contains("Unresolved", model.SelectedChoiceSummary);
        Assert.False(model.ClearConflictCommand.CanExecute(null));
        Assert.True(model.ApplyChoicesCommand.CanExecute(null));

        model.ApplyChoicesCommand.Execute(null);

        Assert.Equal("NeedsDecision", model.PlanStatus);
        Assert.Equal(new[] { "source", "destination" }, calls);
    }

    [Fact]
    public async Task FailedPlanUpdateDoesNotRebuildThePreviousPreview()
    {
        var planner = new ThrowOnSecondPlanCallPlanner();
        var previewer = new CountingPreviewer(new MigrationPreview(
            MigrationPlanStatus.NeedsDecision,
            [new MigrationPreviewEntry(
                "config",
                true,
                EntryState.Directory,
                EntryState.Directory,
                MigrationPreviewAction.NeedsDecision,
                MigrationPlanDisposition.DestinationConflict,
                false)],
            [],
            [new ConflictDecisionIssue("config", ConflictDecisionIssueKind.NoDestinationConflict)]));
        var model = CreateModel(
            new StubInspector((path, _) => Task.FromResult(
                Inspection(("config", EntryState.Directory)))),
            planner,
            previewer);
        model.SourcePath = "source";
        model.DestinationPath = "destination";

        await model.GeneratePreviewAsync();
        model.SelectedEntry = Entry(model, "config");
        model.SkipConflictCommand.Execute(null);
        model.ApplyChoicesCommand.Execute(null);

        Assert.Equal("NeedsDecision", model.PlanStatus);
        Assert.Contains("PlanFailed", model.Status);
        Assert.Equal(1, previewer.Calls);
    }

    [Fact]
    public async Task IncludeAndExcludeCommandsEditOnlyTheSelectedRow()
    {
        var inspector = new StubInspector((path, _) => Task.FromResult(
            path == "source"
                ? Inspection(("config", EntryState.Directory), ("saves", EntryState.Directory))
                : Inspection()));
        var model = CreateModel(inspector);
        model.SourcePath = "source";
        model.DestinationPath = "destination";
        await model.GeneratePreviewAsync();

        MigrationSelectionEntryViewModel saves = Entry(model, "saves");
        model.SelectedEntry = saves;
        Assert.True(model.IncludeSelectedCommand.CanExecute(null));
        model.IncludeSelectedCommand.Execute(null);
        Assert.True(saves.Selected);
        Assert.True(model.ExcludeSelectedCommand.CanExecute(null));

        model.ExcludeSelectedCommand.Execute(null);

        Assert.False(saves.Selected);
        Assert.True(model.IncludeSelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task ConflictCommandsAreDisabledForNonConflictRows()
    {
        var inspector = new StubInspector((path, _) => Task.FromResult(
            path == "source"
                ? Inspection(("config", EntryState.Directory))
                : Inspection()));
        var model = CreateModel(inspector);
        model.SourcePath = "source";
        model.DestinationPath = "destination";
        await model.GeneratePreviewAsync();

        model.SelectedEntry = Entry(model, "config");

        Assert.False(model.SkipConflictCommand.CanExecute(null));
        Assert.False(model.ReplaceConflictCommand.CanExecute(null));
        Assert.False(model.ClearConflictCommand.CanExecute(null));
    }

    [Fact]
    public async Task ResetRecommendedRestoresDefaultSelection()
    {
        var inspector = new StubInspector((path, _) => Task.FromResult(
            path == "source"
                ? Inspection(("config", EntryState.Directory), ("saves", EntryState.Directory))
                : Inspection()));
        var model = CreateModel(inspector);
        model.SourcePath = "source";
        model.DestinationPath = "destination";
        await model.GeneratePreviewAsync();

        model.SelectedEntry = Entry(model, "config");
        model.ExcludeSelectedCommand.Execute(null);
        model.SelectedEntry = Entry(model, "saves");
        model.IncludeSelectedCommand.Execute(null);
        model.ResetRecommendedCommand.Execute(null);

        Assert.True(Entry(model, "config").Selected);
        Assert.False(Entry(model, "saves").Selected);
        Assert.Equal(MigrationPreviewAction.Excluded, Entry(model, "saves").Action);
        Assert.False(model.HasPendingChoices);
    }

    [Fact]
    public async Task ChangingInputClearsPreviousPreviewAndChoices()
    {
        var model = CreateModel(new StubInspector((_, _) =>
            Task.FromResult(Inspection(("config", EntryState.Directory)))));
        model.SourcePath = "source";
        model.DestinationPath = "destination";

        await model.GeneratePreviewAsync();
        model.SelectedEntry = Entry(model, "config");
        model.ExcludeSelectedCommand.Execute(null);
        Assert.True(model.HasPendingChoices);

        model.DestinationPath = "new-destination";

        Assert.Empty(model.Entries);
        Assert.Equal("Not generated", model.PlanStatus);
        Assert.False(model.HasPendingChoices);
    }

    [Fact]
    public async Task CancellationBetweenInspectionsDoesNotPublishLatePreview()
    {
        var pending = new TaskCompletionSource<InstanceInspectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken received = default;
        int calls = 0;
        var inspector = new StubInspector((_, token) =>
        {
            calls++;
            received = token;
            return pending.Task;
        });
        var model = CreateModel(inspector);
        model.SourcePath = "source";
        model.DestinationPath = "destination";

        Task running = model.GeneratePreviewAsync();
        Assert.True(model.IsBusy);
        model.CancelCommand.Execute(null);
        Assert.True(received.IsCancellationRequested);
        pending.SetResult(Inspection(("config", EntryState.Directory)));
        await running;

        Assert.Equal(1, calls);
        Assert.Empty(model.Entries);
        Assert.Equal("Not generated", model.PlanStatus);
        Assert.Contains("cancelled", model.Status);
    }

    [Fact]
    public async Task WorkflowFailureCategoryIsShownWithoutLeakingPrivateMessage()
    {
        var model = CreateModel(new StubInspector((_, _) =>
            throw new InvalidOperationException("private source path")));
        model.SourcePath = "source";
        model.DestinationPath = "destination";

        await model.GeneratePreviewAsync();

        Assert.Contains("InspectionFailed", model.Status);
        Assert.DoesNotContain("private", model.Status);
        Assert.Empty(model.Entries);
    }

    [Fact]
    public void BrowseCommandsUsePurposeSpecificTitles()
    {
        var titles = new List<string>();
        var model = new MigrationPreviewViewModel(
            CreateWorkflow(new StubInspector((_, _) => throw new InvalidOperationException())),
            title =>
            {
                titles.Add(title);
                return title.Contains("source", StringComparison.OrdinalIgnoreCase) ? "source" : "destination";
            });

        model.BrowseSourceCommand.Execute(null);
        model.BrowseDestinationCommand.Execute(null);

        Assert.Equal("source", model.SourcePath);
        Assert.Equal("destination", model.DestinationPath);
        Assert.Equal(2, titles.Count);
        Assert.True(model.GeneratePreviewCommand.CanExecute(null));
    }

    private static MigrationPreviewViewModel CreateModel(IInstanceInspector inspector) =>
        new(CreateWorkflow(inspector), _ => null);

    private static MigrationPreviewViewModel CreateExecutableModel(
        List<string> calls,
        IMigrationExecutionConfirmation confirmation,
        bool destinationConflict = false,
        BackupExecutionResult? backupResult = null,
        IExecutionOrchestrator? execution = null,
        IInstanceInspector? inspector = null,
        IMigrationRecoveryCoordinator? recovery = null,
        IMigrationRollbackConfirmation? rollbackConfirmation = null,
        MigrationReportViewModel? reportViewModel = null,
        IBackupPlanner? backupPlanner = null)
    {
        return new MigrationPreviewViewModel(
            new MigrationWorkflow(
                inspector ?? new StubInspector((path, _) => Task.FromResult(
                    path == "source"
                        ? Inspection(("config", EntryState.Directory))
                        : destinationConflict
                            ? Inspection(("config", EntryState.Directory))
                            : Inspection())),
                new MigrationPlanner(),
                new MigrationPreviewer(),
                backupPlanner ?? new BackupPlanner(),
                new RecordingBackupExecutor(calls, backupResult),
                execution ?? new RecordingExecutionOrchestrator(calls)),
            _ => null,
            confirmation,
            recovery,
            rollbackConfirmation,
            reportViewModel);
    }

    private static MigrationPreviewViewModel CreateModel(
        IInstanceInspector inspector,
        IMigrationPlanner planner,
        IMigrationPreviewer previewer) =>
        new(
            new MigrationWorkflow(
                inspector,
                planner,
                previewer,
                new BackupPlanner(),
                new NeverCalledBackupExecutor(),
                new NeverCalledExecutionOrchestrator()),
            _ => null);

    private static IMigrationWorkflow CreateWorkflow(IInstanceInspector inspector)
    {
        var backupPlanner = new BackupPlanner();
        return new MigrationWorkflow(
            inspector,
            new MigrationPlanner(),
            new MigrationPreviewer(),
            backupPlanner,
            new NeverCalledBackupExecutor(),
            new NeverCalledExecutionOrchestrator());
    }

    private static MigrationSelectionEntryViewModel Entry(MigrationPreviewViewModel model, string name) =>
        Assert.Single(model.Entries, entry => entry.Name == name);

    private static InstanceInspectionResult Inspection(params (string Name, EntryState State)[] overrides)
    {
        var states = overrides.ToDictionary(item => item.Name, item => item.State, StringComparer.Ordinal);
        return new InstanceInspectionResult(
            EntryState.Directory,
            KnownEntryCatalog.All.Select(candidate =>
                new EntryObservation(
                    candidate.Name,
                    candidate.ExpectedKind,
                    states.GetValueOrDefault(candidate.Name, EntryState.Missing))));
    }

    private sealed class StubInspector(
        Func<string, CancellationToken, Task<InstanceInspectionResult>> run) : IInstanceInspector
    {
        public Task<InstanceInspectionResult> InspectAsync(
            string candidatePath,
            CancellationToken cancellationToken = default) =>
            run(candidatePath, cancellationToken);
    }

    private sealed class NeverCalledBackupExecutor : IBackupExecutor
    {
        public Task<BackupExecutionResult> ExecuteAsync(
            string destinationRoot,
            string backupParent,
            BackupPlan plan,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Preview UI must not execute backup IO.");
    }

    private sealed class ThrowingBackupPlanner : IBackupPlanner
    {
        public BackupPlan CreateBackupPlan(MigrationPlan migrationPlan) =>
            throw new InvalidOperationException("private backup path");

        public BackupManifestDraft CreateManifestDraft(BackupPlan backupPlan) =>
            throw new InvalidOperationException("private backup path");
    }

    private sealed class ThrowingReportProjector : IMigrationReportProjector
    {
        public MigrationReportCreationResult Create(MigrationReportEvidence evidence) =>
            throw new InvalidOperationException("private report path");
    }

    private sealed class NeverCalledExecutionOrchestrator : IExecutionOrchestrator
    {
        public Task<ExecutionOrchestrationResult> ExecuteAsync(
            ExecutionOrchestrationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Preview UI must not execute migration IO.");
    }

    private sealed class RecordingBackupExecutor(
        List<string> calls,
        BackupExecutionResult? result = null) : IBackupExecutor
    {
        public Task<BackupExecutionResult> ExecuteAsync(
            string destinationRoot,
            string backupParent,
            BackupPlan plan,
            CancellationToken cancellationToken = default)
        {
            calls.Add("backup");
            return Task.FromResult(result ?? new BackupExecutionResult(
                BackupExecutionStatus.Completed,
                "backup-root"));
        }
    }

    private sealed class RecordingExecutionOrchestrator(
        List<string> calls,
        ExecutionOrchestrationStatus status = ExecutionOrchestrationStatus.Completed) : IExecutionOrchestrator
    {
        public Task<ExecutionOrchestrationResult> ExecuteAsync(
            ExecutionOrchestrationRequest request,
            CancellationToken cancellationToken = default)
        {
            calls.Add("execute");
            return Task.FromResult(new ExecutionOrchestrationResult(
                status,
                Journal: status == ExecutionOrchestrationStatus.RecoveryRequired
                    ? new ExecutionJournalReference("journal", "journal-file")
                    : null,
                AppliedSteps: 1));
        }
    }

    private sealed class PendingExecutionOrchestrator(Task<ExecutionOrchestrationResult> result)
        : IExecutionOrchestrator
    {
        public int Calls { get; private set; }

        public Task<ExecutionOrchestrationResult> ExecuteAsync(
            ExecutionOrchestrationRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return result;
        }
    }

    private sealed class ThrowingPrivateExecutionOrchestrator : IExecutionOrchestrator
    {
        public Task<ExecutionOrchestrationResult> ExecuteAsync(
            ExecutionOrchestrationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("private destination path");
    }

    private sealed class CancellableExecutionOrchestrator : IExecutionOrchestrator
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ExecutionOrchestrationResult> ExecuteAsync(
            ExecutionOrchestrationRequest request,
            CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Unreachable.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new ExecutionOrchestrationResult(ExecutionOrchestrationStatus.Cancelled);
            }
        }
    }

    private sealed class StubConfirmation(bool confirmed) : IMigrationExecutionConfirmation
    {
        public List<MigrationExecutionConfirmation> Requests { get; } = [];

        public bool Confirm(MigrationExecutionConfirmation request)
        {
            Requests.Add(request);
            return confirmed;
        }
    }

    private static MigrationRecoveryDiagnosis RollbackableDiagnosis()
    {
        var plan = new RollbackPlan(
            RollbackPlanStatus.Ready,
            [new RollbackPlanEntry(
                0, "config", ExpectedEntryKind.Directory, ExecutionOperationKind.Copy,
                RollbackActionKind.DeleteCreatedEntry,
                new ExecutionContentFingerprint(1, 0, 1, new string('A', 64)))],
            []);
        return new MigrationRecoveryDiagnosis(
            MigrationRecoveryDiagnosisStatus.RollbackAvailable,
            plan,
            AppliedExecutionSteps: 1,
            DeleteCreatedEntryCount: 1);
    }

    private sealed class StubRollbackConfirmation(bool confirmed) : IMigrationRollbackConfirmation
    {
        public List<MigrationRollbackConfirmation> Requests { get; } = [];
        public bool Confirm(MigrationRollbackConfirmation request)
        {
            Requests.Add(request);
            return confirmed;
        }
    }

    private sealed class StubRecoveryCoordinator(
        MigrationRecoveryDiagnosis diagnosis,
        MigrationRecoveryResult? result = null) : IMigrationRecoveryCoordinator
    {
        public int DiagnoseCalls { get; private set; }
        public int ExecuteCalls { get; private set; }
        public Task<MigrationRecoveryDiagnosis> DiagnoseAsync(MigrationRecoveryRequest request, CancellationToken cancellationToken = default)
        {
            DiagnoseCalls++;
            return Task.FromResult(diagnosis);
        }
        public Task<MigrationRecoveryResult> ExecuteAsync(MigrationRecoveryRequest request, MigrationRecoveryDiagnosis actual, CancellationToken cancellationToken = default)
        {
            ExecuteCalls++;
            return Task.FromResult(result ?? new MigrationRecoveryResult(MigrationRecoveryOutcome.Applied));
        }
    }

    private sealed class PendingRecoveryCoordinator(
        MigrationRecoveryDiagnosis diagnosis,
        Task<MigrationRecoveryResult> result) : IMigrationRecoveryCoordinator
    {
        public int ExecuteCalls { get; private set; }
        public Task<MigrationRecoveryDiagnosis> DiagnoseAsync(MigrationRecoveryRequest request, CancellationToken cancellationToken = default) => Task.FromResult(diagnosis);
        public Task<MigrationRecoveryResult> ExecuteAsync(MigrationRecoveryRequest request, MigrationRecoveryDiagnosis actual, CancellationToken cancellationToken = default)
        {
            ExecuteCalls++;
            return result;
        }
    }

    private sealed class ThrowingRecoveryCoordinator : IMigrationRecoveryCoordinator
    {
        public Task<MigrationRecoveryDiagnosis> DiagnoseAsync(MigrationRecoveryRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("private recovery path");
        public Task<MigrationRecoveryResult> ExecuteAsync(MigrationRecoveryRequest request, MigrationRecoveryDiagnosis diagnosis, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("private rollback path");
    }

    private sealed class ThrowOnSecondPlanCallPlanner : IMigrationPlanner
    {
        private int calls;

        public MigrationPlan CreatePlan(
            InstanceInspectionResult source,
            InstanceInspectionResult destination,
            IEnumerable<string> selectedEntryNames,
            IReadOnlyDictionary<string, DestinationConflictDecision>? conflictDecisions = null)
        {
            if (++calls > 1)
            {
                throw new InvalidOperationException("planned failure");
            }

            return new MigrationPlanner().CreatePlan(
                source,
                destination,
                selectedEntryNames,
                conflictDecisions);
        }

        public MigrationPlan CreateRecommendedPlan(
            InstanceInspectionResult source,
            InstanceInspectionResult destination,
            IReadOnlyDictionary<string, DestinationConflictDecision>? conflictDecisions = null) =>
            new MigrationPlanner().CreateRecommendedPlan(source, destination, conflictDecisions);
    }

    private sealed class CountingPreviewer(MigrationPreview preview) : IMigrationPreviewer
    {
        public int Calls { get; private set; }

        public MigrationPreview CreatePreview(MigrationPlan plan)
        {
            Calls++;
            return preview;
        }
    }
}
