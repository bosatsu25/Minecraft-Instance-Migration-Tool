using System.ComponentModel;
using System.Runtime.CompilerServices;
using MinecraftInstanceMigration.Application.Workflow;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.App.Presentation;

public sealed class MigrationPreviewViewModel : INotifyPropertyChanged
{
    private readonly IMigrationWorkflow workflow;
    private readonly Func<string, string?> chooseFolder;
    private CancellationTokenSource? cancellation;
    private string sourcePath = "";
    private string destinationPath = "";
    private MigrationWorkflowSession? session;
    private MigrationPreview? preview;
    private IReadOnlyList<MigrationSelectionEntryViewModel> choices = [];
    private MigrationSelectionEntryViewModel? selectedEntry;
    private bool hasPendingChoices;
    private string status = "Choose source and destination folders, then generate a dry-run preview.";

    public MigrationPreviewViewModel(
        IMigrationWorkflow workflow,
        Func<string, string?> chooseFolder)
    {
        this.workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        this.chooseFolder = chooseFolder ?? throw new ArgumentNullException(nameof(chooseFolder));

        BrowseSourceCommand = new RelayCommand(
            () => Browse("Choose source instance candidate folder", path => SourcePath = path),
            () => !IsBusy);
        BrowseDestinationCommand = new RelayCommand(
            () => Browse("Choose destination instance candidate folder", path => DestinationPath = path),
            () => !IsBusy);
        GeneratePreviewCommand = new RelayCommand(
            async () => await GeneratePreviewAsync(),
            () => !IsBusy && !string.IsNullOrWhiteSpace(SourcePath) && !string.IsNullOrWhiteSpace(DestinationPath));
        ApplyChoicesCommand = new RelayCommand(
            ApplyChoices,
            () => !IsBusy && preview is not null && hasPendingChoices);
        ResetRecommendedCommand = new RelayCommand(
            ResetRecommended,
            () => !IsBusy && preview is not null);
        IncludeSelectedCommand = new RelayCommand(
            () => SetSelectedIncluded(true),
            () => !IsBusy && SelectedChoice is { Selected: false });
        ExcludeSelectedCommand = new RelayCommand(
            () => SetSelectedIncluded(false),
            () => !IsBusy && SelectedChoice?.Selected == true);
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
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand BrowseSourceCommand { get; }

    public RelayCommand BrowseDestinationCommand { get; }

    public RelayCommand GeneratePreviewCommand { get; }

    public RelayCommand ApplyChoicesCommand { get; }

    public RelayCommand ResetRecommendedCommand { get; }

    public RelayCommand IncludeSelectedCommand { get; }

    public RelayCommand ExcludeSelectedCommand { get; }

    public RelayCommand SkipConflictCommand { get; }

    public RelayCommand ReplaceConflictCommand { get; }

    public RelayCommand ClearConflictCommand { get; }

    public RelayCommand CancelCommand { get; }

    public bool IsBusy => cancellation is not null;

    public bool CanEditPaths => !IsBusy;

    public bool HasPendingChoices => hasPendingChoices;

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
        }
    }

    public void Cancel() => cancellation?.Cancel();

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

            if (current.State != MigrationWorkflowState.Preview)
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
        !IsBusy && SelectedChoice?.CanChooseConflict == true;

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
        if (IsBusy || field == value)
        {
            return;
        }

        field = value;
        session = null;
        ClearPreview();
        status = "Ready to generate a new dry-run preview. Previous preview and choices cleared.";
        Notify(nameof(SourcePath));
        Notify(nameof(DestinationPath));
        NotifyPreview();
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
    }

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
