using System.ComponentModel;
using System.Runtime.CompilerServices;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Application.Workflow;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.App.Presentation;

public sealed class MigrationPreviewViewModel : INotifyPropertyChanged
{
    private readonly IMigrationWorkflow workflow;
    private readonly Func<string, string?> chooseFolder;
    private readonly IMigrationExecutionConfirmation executionConfirmation;
    private readonly IMigrationRecoveryCoordinator? recoveryCoordinator;
    private readonly IMigrationRollbackConfirmation rollbackConfirmation;
    private CancellationTokenSource? cancellation;
    private string sourcePath = "";
    private string destinationPath = "";
    private string safetyWorkspacePath = "";
    private MigrationWorkflowSession? session;
    private MigrationPreview? preview;
    private IReadOnlyList<MigrationSelectionEntryViewModel> choices = [];
    private MigrationSelectionEntryViewModel? selectedEntry;
    private bool hasPendingChoices;
    private MigrationRecoveryDiagnosis? recoveryDiagnosis;
    private MigrationRecoveryResult? recoveryResult;
    private string recoveryStatus = "No recovery diagnosis is active.";
    private string status = "Choose source and destination folders, then generate a dry-run preview.";

    public MigrationPreviewViewModel(
        IMigrationWorkflow workflow,
        Func<string, string?> chooseFolder,
        IMigrationExecutionConfirmation? executionConfirmation = null,
        IMigrationRecoveryCoordinator? recoveryCoordinator = null,
        IMigrationRollbackConfirmation? rollbackConfirmation = null)
    {
        this.workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        this.chooseFolder = chooseFolder ?? throw new ArgumentNullException(nameof(chooseFolder));
        this.executionConfirmation = executionConfirmation ?? RejectingMigrationExecutionConfirmation.Instance;
        this.recoveryCoordinator = recoveryCoordinator;
        this.rollbackConfirmation = rollbackConfirmation ?? RejectingMigrationRollbackConfirmation.Instance;

        BrowseSourceCommand = new RelayCommand(
            () => Browse("Choose source instance candidate folder", path => SourcePath = path),
            () => CanEditPaths);
        BrowseDestinationCommand = new RelayCommand(
            () => Browse("Choose destination instance candidate folder", path => DestinationPath = path),
            () => CanEditPaths);
        BrowseSafetyWorkspaceCommand = new RelayCommand(
            () => Browse("Choose safety workspace for journal and backup", path => SafetyWorkspacePath = path),
            () => CanEditPaths);
        GeneratePreviewCommand = new RelayCommand(
            async () => await GeneratePreviewAsync(),
            () => CanEditPaths &&
                !string.IsNullOrWhiteSpace(SourcePath) &&
                !string.IsNullOrWhiteSpace(DestinationPath));
        ApplyChoicesCommand = new RelayCommand(
            ApplyChoices,
            () => CanEditChoices && preview is not null && hasPendingChoices);
        ResetRecommendedCommand = new RelayCommand(
            ResetRecommended,
            () => CanEditChoices && preview is not null);
        IncludeSelectedCommand = new RelayCommand(
            () => SetSelectedIncluded(true),
            () => CanEditChoices && SelectedChoice is { Selected: false });
        ExcludeSelectedCommand = new RelayCommand(
            () => SetSelectedIncluded(false),
            () => CanEditChoices && SelectedChoice?.Selected == true);
        SkipConflictCommand = new RelayCommand(
            () => SetSelectedConflict(DestinationConflictDecision.Skip),
            CanSetSelectedConflict);
        ReplaceConflictCommand = new RelayCommand(
            () => SetSelectedConflict(DestinationConflictDecision.Replace),
            CanSetSelectedConflict);
        ClearConflictCommand = new RelayCommand(
            () => SetSelectedConflict(DestinationConflictDecision.Unresolved),
            () => CanSetSelectedConflict() &&
                SelectedChoice?.ConflictDecision != DestinationConflictDecision.Unresolved);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        ExecuteMigrationCommand = new RelayCommand(
            async () => await ExecuteMigrationAsync(),
            CanExecuteMigration);
        RollbackMigrationCommand = new RelayCommand(
            async () => await RollbackMigrationAsync(),
            CanRollbackMigration);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand BrowseSourceCommand { get; }

    public RelayCommand BrowseDestinationCommand { get; }

    public RelayCommand BrowseSafetyWorkspaceCommand { get; }

    public RelayCommand GeneratePreviewCommand { get; }

    public RelayCommand ApplyChoicesCommand { get; }

    public RelayCommand ResetRecommendedCommand { get; }

    public RelayCommand IncludeSelectedCommand { get; }

    public RelayCommand ExcludeSelectedCommand { get; }

    public RelayCommand SkipConflictCommand { get; }

    public RelayCommand ReplaceConflictCommand { get; }

    public RelayCommand ClearConflictCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand ExecuteMigrationCommand { get; }

    public RelayCommand RollbackMigrationCommand { get; }

    public bool IsBusy => cancellation is not null;

    public bool CanEditPaths =>
        !IsBusy && session?.State != MigrationWorkflowState.RecoveryRequired;

    public bool HasPendingChoices => hasPendingChoices;

    public MigrationWorkflowState WorkflowState =>
        session?.State ?? MigrationWorkflowState.SelectRoots;

    public string SourcePath
    {
        get => sourcePath;
        set => SetPath(ref sourcePath, value);
    }

    public string DestinationPath
    {
        get => destinationPath;
        set => SetPath(ref destinationPath, value);
    }

    public string SafetyWorkspacePath
    {
        get => safetyWorkspacePath;
        set
        {
            if (!CanEditPaths || safetyWorkspacePath == value)
            {
                return;
            }

            safetyWorkspacePath = value;
            Notify();
            ExecuteMigrationCommand.Refresh();
        }
    }

    public IReadOnlyList<MigrationSelectionEntryViewModel> Entries => choices;

    public MigrationSelectionEntryViewModel? SelectedEntry
    {
        get => selectedEntry;
        set
        {
            if (ReferenceEquals(selectedEntry, value))
            {
                return;
            }

            selectedEntry = value;
            Notify();
            NotifySelectedChoice();
            RefreshChoiceCommands();
        }
    }

    public string SelectedChoiceSummary
    {
        get
        {
            MigrationSelectionEntryViewModel? choice = SelectedChoice;
            if (choice is null)
            {
                return "No row selected.";
            }

            string selection = choice.Selected ? "Included" : "Excluded";
            string conflict = choice.CanChooseConflict
                ? choice.ConflictDecision.ToString()
                : "Not applicable";
            return $"{choice.Name}: {selection}; conflict: {conflict}.";
        }
    }

    public string PlanStatus => preview?.Status.ToString() ?? "Not generated";

    public string Status => status;

    public bool IsRecoveryVisible => session?.State == MigrationWorkflowState.RecoveryRequired;

    public string RecoveryDiagnosisStatus => recoveryDiagnosis?.Status.ToString() ?? "Not diagnosed";

    public string RollbackOutcome => recoveryResult?.Outcome.ToString() ?? "Not started";

    public string RecoveryStatus => recoveryStatus;

    public string RecoverySummary => recoveryDiagnosis is null
        ? ""
        : $"Execution evidence — Applied: {recoveryDiagnosis.AppliedExecutionSteps}; " +
          $"Failed: {recoveryDiagnosis.FailedExecutionSteps}; Uncertain: {recoveryDiagnosis.UncertainExecutionSteps}; " +
          $"rollback candidates: {recoveryDiagnosis.RollbackCandidateCount}; " +
          $"delete created: {recoveryDiagnosis.DeleteCreatedEntryCount}; " +
          $"restore backup: {recoveryDiagnosis.RestoreFromBackupCount}; " +
          $"backup required: {recoveryDiagnosis.BackupRequired}.";

    public string Summary => preview is null
        ? ""
        : $"Copy: {preview.CopyCount}; Replace: {preview.ReplaceCount}; Skip: {preview.SkipCount}; " +
          $"No source: {preview.NoSourceCount}; unresolved: {preview.NeedsDecisionCount}; " +
          $"blocked: {preview.BlockedCount}; backup required: {preview.RequiresBackup}.";

    private MigrationSelectionEntryViewModel? SelectedChoice => selectedEntry;

    public async Task GeneratePreviewAsync()
    {
        if (!GeneratePreviewCommand.CanExecute(null))
        {
            return;
        }

        using var operation = new CancellationTokenSource();
        cancellation = operation;
        ClearPreview();
        status = "Inspecting source and destination metadata for dry run…";
        NotifyPreview();
        NotifyBusy();

        try
        {
            MigrationWorkflowSession current = workflow.CreateSession();
            current = workflow.SelectRoots(current, SourcePath, DestinationPath);
            current = await workflow.InspectAsync(current, operation.Token);
            operation.Token.ThrowIfCancellationRequested();

            if (current.State != MigrationWorkflowState.ConfigurePlan)
            {
                session = current;
                status = WorkflowFailureStatus(current, "Dry-run preview inspection could not complete.");
                return;
            }

            current = workflow.ConfigurePlan(current, MigrationSelectionPresets.Recommended);
            current = workflow.CreatePreview(current);
            operation.Token.ThrowIfCancellationRequested();

            session = current;
            PublishPreview(current);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            ClearPreview();
            status = "Dry-run preview cancelled. No result retained.";
        }
        catch (Exception error)
        {
            ClearPreview();
            status = $"Dry-run preview failed ({error.GetType().Name}). No result retained.";
        }
        finally
        {
            cancellation = null;
            NotifyPreview();
            NotifyBusy();
            NotifyExecution();
        }
    }

    public void Cancel() => cancellation?.Cancel();

    public async Task ExecuteMigrationAsync()
    {
        if (!CanExecuteMigration() || session is null || preview is null)
        {
            return;
        }

        using var operation = new CancellationTokenSource();
        cancellation = operation;
        NotifyBusy();

        try
        {
            var request = new MigrationExecutionConfirmation(
                session.SourceRoot ?? "",
                session.DestinationRoot ?? "",
                SafetyWorkspacePath,
                preview.CopyCount,
                preview.ReplaceCount,
                preview.SkipCount,
                preview.RequiresBackup);
            if (!executionConfirmation.Confirm(request))
            {
                status = "Migration was not started. No files were changed.";
                Notify(nameof(Status));
                return;
            }

            status = preview.RequiresBackup
                ? "Preparing and verifying the required backup…"
                : "Preparing execution. No backup is required for this plan…";
            Notify(nameof(Status));

            MigrationWorkflowSession current = workflow.PrepareBackup(
                session,
                preview.RequiresBackup ? SafetyWorkspacePath : null,
                SafetyWorkspacePath);
            if (current.FailureKind is not null)
            {
                session = current;
                status = WorkflowFailureStatus(current, "Migration is blocked before backup.");
                NotifyExecution();
                return;
            }

            current = await workflow.ExecuteBackupAsync(current, operation.Token);
            if (current.State != MigrationWorkflowState.BackupReady)
            {
                session = current;
                PublishExecutionResult(current);
                return;
            }

            current = workflow.PrepareExecution(current);
            if (current.State != MigrationWorkflowState.ReadyForExecution ||
                current.FailureKind is not null)
            {
                session = current;
                PublishExecutionResult(current);
                return;
            }

            status = "Executing migration and independently verifying written content…";
            Notify(nameof(Status));
            current = await workflow.ExecuteAsync(current, operation.Token);
            session = current;
            PublishExecutionResult(current);
            if (current.State == MigrationWorkflowState.RecoveryRequired)
            {
                await DiagnoseRecoveryAsync(operation.Token);
            }
        }
        catch (Exception error)
        {
            status = $"Migration failed ({error.GetType().Name}). Review recovery evidence before retrying.";
            Notify(nameof(Status));
        }
        finally
        {
            cancellation = null;
            NotifyBusy();
            NotifyExecution();
        }
    }

    public async Task RollbackMigrationAsync()
    {
        if (!CanRollbackMigration() || recoveryDiagnosis is null)
        {
            return;
        }

        MigrationRecoveryRequest? request = CreateRecoveryRequest();
        if (request is null)
        {
            recoveryStatus = "Rollback is blocked because recovery evidence is incomplete.";
            NotifyRecovery();
            return;
        }

        var confirmation = new MigrationRollbackConfirmation(
            recoveryDiagnosis.RollbackCandidateCount,
            recoveryDiagnosis.DeleteCreatedEntryCount,
            recoveryDiagnosis.RestoreFromBackupCount,
            recoveryDiagnosis.BackupRequired);
        bool confirmed;
        try
        {
            confirmed = rollbackConfirmation.Confirm(confirmation);
        }
        catch (Exception)
        {
            recoveryStatus = "Rollback confirmation could not be displayed. No rollback changes were made.";
            NotifyRecovery();
            return;
        }

        if (!confirmed)
        {
            recoveryStatus = "Rollback was not started. No rollback changes were made.";
            NotifyRecovery();
            return;
        }

        using var operation = new CancellationTokenSource();
        cancellation = operation;
        recoveryStatus = "Guarded rollback is running. Current evidence is revalidated before each change.";
        NotifyBusy();
        NotifyRecovery();

        try
        {
            recoveryResult = await recoveryCoordinator!.ExecuteAsync(
                request,
                recoveryDiagnosis,
                operation.Token);
            recoveryStatus = RecoveryResultStatus(recoveryResult);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            recoveryResult = new MigrationRecoveryResult(MigrationRecoveryOutcome.Uncertain);
            recoveryStatus = "The rollback outcome cannot be proven. Automatic retry is not performed; manual recovery is required.";
        }
        catch (Exception)
        {
            recoveryResult = new MigrationRecoveryResult(MigrationRecoveryOutcome.Uncertain);
            recoveryStatus = "The rollback outcome cannot be proven. Automatic retry is not performed; manual recovery is required.";
        }
        finally
        {
            cancellation = null;
            NotifyBusy();
            NotifyRecovery();
        }
    }

    private async Task DiagnoseRecoveryAsync(CancellationToken cancellationToken)
    {
        recoveryDiagnosis = null;
        recoveryResult = null;
        MigrationRecoveryRequest? request = CreateRecoveryRequest();
        if (request is null || recoveryCoordinator is null)
        {
            recoveryStatus = "Recovery evidence is incomplete. Automatic rollback is blocked; manual recovery is required.";
            NotifyRecovery();
            return;
        }

        try
        {
            recoveryStatus = "Analyzing durable execution evidence. Rollback will not start automatically.";
            NotifyRecovery();
            recoveryDiagnosis = await recoveryCoordinator.DiagnoseAsync(request, cancellationToken);
            recoveryStatus = RecoveryDiagnosisStatusText(recoveryDiagnosis);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            recoveryStatus = "Recovery diagnosis was cancelled. Rollback remains blocked.";
        }
        catch (Exception)
        {
            recoveryStatus = "Recovery diagnosis failed. Rollback remains blocked; technical details are not displayed.";
        }

        NotifyRecovery();
    }

    private MigrationRecoveryRequest? CreateRecoveryRequest()
    {
        if (session?.State != MigrationWorkflowState.RecoveryRequired ||
            session.DestinationRoot is null ||
            session.JournalParent is null ||
            session.MigrationPlan is null ||
            session.BackupPlan is null ||
            session.ExecutionResult?.Journal is null)
        {
            return null;
        }

        return new MigrationRecoveryRequest(
            session.DestinationRoot,
            session.BackupResult?.BackupRootPath,
            session.BackupPlan,
            session.JournalParent,
            session.MigrationPlan,
            session.ExecutionResult.Journal);
    }

    private void ApplyChoices()
    {
        if (session is null || preview is null || IsBusy)
        {
            return;
        }

        string[] selected = choices
            .Where(choice => choice.Selected)
            .Select(choice => choice.Name)
            .ToArray();

        var decisions = choices
            .Where(choice =>
                choice.Selected &&
                choice.CanChooseConflict &&
                choice.ConflictDecision != DestinationConflictDecision.Unresolved)
            .ToDictionary(
                choice => choice.Name,
                choice => choice.ConflictDecision,
                StringComparer.Ordinal);

        try
        {
            MigrationWorkflowSession current = workflow.ConfigurePlan(
                session,
                selected,
                decisions);

            if (current.State != MigrationWorkflowState.Preview ||
                current.FailureKind is not null ||
                current.MigrationPlan is null)
            {
                session = current;
                status = WorkflowFailureStatus(current, "Choices could not be applied.");
                return;
            }

            current = workflow.CreatePreview(current);
            session = current;
            PublishPreview(current, choicesApplied: true);
        }
        catch (Exception error)
        {
            status = $"Applying choices failed ({error.GetType().Name}). Previous preview retained.";
            Notify(nameof(Status));
        }
    }

    private void ResetRecommended()
    {
        if (session is null || preview is null || IsBusy)
        {
            return;
        }

        var recommended = MigrationSelectionPresets.Recommended.ToHashSet(StringComparer.Ordinal);
        foreach (MigrationSelectionEntryViewModel choice in choices)
        {
            choice.Selected = recommended.Contains(choice.Name);
            if (choice.ConflictDecision != DestinationConflictDecision.Unresolved)
            {
                choice.ConflictDecision = DestinationConflictDecision.Unresolved;
            }
        }

        ApplyChoices();
    }

    private void SetSelectedIncluded(bool included)
    {
        MigrationSelectionEntryViewModel? choice = SelectedChoice;
        if (IsBusy || choice is null)
        {
            return;
        }

        choice.Selected = included;
        NotifySelectedChoice();
        RefreshChoiceCommands();
    }

    private bool CanSetSelectedConflict() =>
        CanEditChoices && SelectedChoice?.CanChooseConflict == true;

    private bool CanEditChoices =>
        !IsBusy &&
        session?.State is MigrationWorkflowState.Preview or
            MigrationWorkflowState.ReadyForBackup;

    private bool CanExecuteMigration() =>
        !IsBusy &&
        !hasPendingChoices &&
        !string.IsNullOrWhiteSpace(SafetyWorkspacePath) &&
        preview?.Status == MigrationPlanStatus.Ready &&
        session?.CanPrepareBackup == true;

    private bool CanRollbackMigration() =>
        !IsBusy &&
        recoveryResult is null &&
        recoveryCoordinator is not null &&
        session?.State == MigrationWorkflowState.RecoveryRequired &&
        recoveryDiagnosis?.CanRollback == true;

    private void PublishExecutionResult(MigrationWorkflowSession current)
    {
        status = current.State switch
        {
            MigrationWorkflowState.Completed =>
                $"Migration completed. Copied: {preview?.CopyCount ?? 0}; " +
                $"Replaced: {preview?.ReplaceCount ?? 0}; Skipped: {preview?.SkipCount ?? 0}. " +
                "Verification succeeded.",
            MigrationWorkflowState.Cancelled =>
                "Migration cancelled before execution completed. No further work was started.",
            MigrationWorkflowState.RecoveryRequired =>
                "RecoveryRequired. Do not execute again; review recovery evidence before changing files.",
            MigrationWorkflowState.Blocked =>
                ExecutionFailureStatus(current, "Migration is blocked."),
            _ => ExecutionFailureStatus(current, "Migration could not continue."),
        };
        NotifyExecution();
    }

    private void NotifyExecution()
    {
        Notify(nameof(WorkflowState));
        Notify(nameof(Status));
        ExecuteMigrationCommand.Refresh();
        RollbackMigrationCommand.Refresh();
    }

    private void SetSelectedConflict(DestinationConflictDecision decision)
    {
        MigrationSelectionEntryViewModel? choice = SelectedChoice;
        if (!CanSetSelectedConflict() || choice is null)
        {
            return;
        }

        choice.ConflictDecision = decision;
        NotifySelectedChoice();
        RefreshChoiceCommands();
    }

    private void PublishPreview(
        MigrationWorkflowSession current,
        bool choicesApplied = false)
    {
        preview = current.MigrationPreview;
        if (preview is null)
        {
            ClearPreview();
            status = WorkflowFailureStatus(current, "Dry-run preview was not produced.");
            return;
        }

        string? selectedName = selectedEntry?.Name;
        choices = preview.Entries
            .Select(entry => new MigrationSelectionEntryViewModel(
                entry,
                current.ConflictDecisions.TryGetValue(entry.Name, out DestinationConflictDecision decision)
                    ? decision
                    : DestinationConflictDecision.Unresolved,
                MarkChoicesChanged))
            .ToArray();
        selectedEntry = selectedName is null
            ? null
            : choices.FirstOrDefault(choice =>
                string.Equals(choice.Name, selectedName, StringComparison.Ordinal));

        hasPendingChoices = false;
        status = preview.Status switch
        {
            MigrationPlanStatus.Ready when choicesApplied =>
                "Choices applied. Dry-run preview is ready. No files were changed.",
            MigrationPlanStatus.Ready =>
                "Dry-run preview ready. Select a row to edit migration choices. No files were changed.",
            MigrationPlanStatus.NeedsDecision =>
                "Dry-run preview needs conflict decisions. Select a conflict row, choose Skip or Replace, then apply choices. No files were changed.",
            _ =>
                "Dry-run preview is blocked. Review the listed states; no files were changed.",
        };

        NotifyPreview();
        Notify(nameof(SelectedEntry));
        NotifySelectedChoice();
        RefreshChoiceCommands();
    }

    private static string WorkflowFailureStatus(
        MigrationWorkflowSession current,
        string prefix) =>
        current.FailureKind is null
            ? prefix
            : $"{prefix} ({current.FailureKind}). No files were changed.";

    private static string ExecutionFailureStatus(
        MigrationWorkflowSession current,
        string prefix) =>
        current.FailureKind is null
            ? prefix
            : $"{prefix} ({current.FailureKind}). Review the safety workspace before retrying.";

    private void MarkChoicesChanged()
    {
        if (preview is null)
        {
            return;
        }

        hasPendingChoices = true;
        status = "Selection or conflict choices changed. Apply choices to refresh the dry-run preview.";
        Notify(nameof(HasPendingChoices));
        Notify(nameof(Status));
        NotifySelectedChoice();
        RefreshChoiceCommands();
    }

    private void Browse(string title, Action<string> apply)
    {
        string? selected = chooseFolder(title);
        if (selected is not null)
        {
            apply(selected);
        }
    }

    private void SetPath(ref string field, string value)
    {
        if (!CanEditPaths || field == value)
        {
            return;
        }

        field = value;
        session = null;
        recoveryDiagnosis = null;
        recoveryResult = null;
        recoveryStatus = "No recovery diagnosis is active.";
        ClearPreview();
        status = "Ready to generate a new dry-run preview. Previous preview and choices cleared.";
        Notify(nameof(SourcePath));
        Notify(nameof(DestinationPath));
        NotifyPreview();
        NotifyExecution();
        NotifyRecovery();
        GeneratePreviewCommand.Refresh();
    }

    private void ClearPreview()
    {
        preview = null;
        choices = [];
        selectedEntry = null;
        hasPendingChoices = false;
        Notify(nameof(SelectedEntry));
        NotifySelectedChoice();
        RefreshChoiceCommands();
    }

    private void NotifyPreview()
    {
        Notify(nameof(Entries));
        Notify(nameof(PlanStatus));
        Notify(nameof(Summary));
        Notify(nameof(Status));
        Notify(nameof(HasPendingChoices));
    }

    private void NotifySelectedChoice() =>
        Notify(nameof(SelectedChoiceSummary));

    private void NotifyBusy()
    {
        Notify(nameof(IsBusy));
        Notify(nameof(CanEditPaths));
        BrowseSourceCommand.Refresh();
        BrowseDestinationCommand.Refresh();
        GeneratePreviewCommand.Refresh();
        CancelCommand.Refresh();
        BrowseSafetyWorkspaceCommand.Refresh();
        ExecuteMigrationCommand.Refresh();
        RollbackMigrationCommand.Refresh();
        RefreshChoiceCommands();
    }

    private void RefreshChoiceCommands()
    {
        ApplyChoicesCommand.Refresh();
        ResetRecommendedCommand.Refresh();
        IncludeSelectedCommand.Refresh();
        ExcludeSelectedCommand.Refresh();
        SkipConflictCommand.Refresh();
        ReplaceConflictCommand.Refresh();
        ClearConflictCommand.Refresh();
        ExecuteMigrationCommand.Refresh();
        RollbackMigrationCommand.Refresh();
    }

    private void NotifyRecovery()
    {
        Notify(nameof(IsRecoveryVisible));
        Notify(nameof(RecoveryDiagnosisStatus));
        Notify(nameof(RecoverySummary));
        Notify(nameof(RollbackOutcome));
        Notify(nameof(RecoveryStatus));
        RollbackMigrationCommand.Refresh();
    }

    private static string RecoveryDiagnosisStatusText(MigrationRecoveryDiagnosis diagnosis) =>
        diagnosis.Status switch
        {
            MigrationRecoveryDiagnosisStatus.RollbackAvailable =>
                "Recovery is required. Guarded rollback is available after explicit confirmation; it will not start automatically.",
            MigrationRecoveryDiagnosisStatus.NotRequired =>
                "No rollback action is required. Automatic execution retry is not performed.",
            MigrationRecoveryDiagnosisStatus.ManualRecoveryRequired =>
                "Automatic rollback cannot be proven safe. Manual recovery is required; no automatic retry is performed.",
            MigrationRecoveryDiagnosisStatus.Cancelled =>
                "Recovery diagnosis was cancelled. Rollback remains blocked.",
            _ =>
                "Rollback is blocked because the required safety evidence could not be validated.",
        };

    private static string RecoveryResultStatus(MigrationRecoveryResult result) =>
        result.Outcome switch
        {
            MigrationRecoveryOutcome.Applied =>
                "Recovered. Every planned rollback action has durable Applied evidence.",
            MigrationRecoveryOutcome.GuardRejected =>
                result.AppliedActions == 0
                    ? "GuardRejected. The current destination no longer matches the expected migration state and was not modified by rollback."
                    : $"GuardRejected after {result.AppliedActions} rollback action(s) were applied. The rejected destination was not modified; manual recovery is required.",
            MigrationRecoveryOutcome.Failed =>
                "Rollback failed. Durable failure evidence was recorded; manual recovery is required.",
            MigrationRecoveryOutcome.Uncertain =>
                "The previous rollback outcome cannot be proven. Automatic retry is not performed; manual recovery is required.",
            MigrationRecoveryOutcome.Cancelled =>
                "Rollback was cancelled before a safe completion could be established.",
            _ =>
                "Rollback was blocked before a safe recovery could be completed.",
        };

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
