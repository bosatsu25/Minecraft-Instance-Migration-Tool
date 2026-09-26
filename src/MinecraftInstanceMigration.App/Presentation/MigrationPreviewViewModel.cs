using System.ComponentModel;
using System.Runtime.CompilerServices;
using MinecraftInstanceMigration.Application.Inspection;
using MinecraftInstanceMigration.Application.Planning;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.App.Presentation;

public sealed class MigrationPreviewViewModel : INotifyPropertyChanged
{
    private readonly IInstanceInspector inspector;
    private readonly IMigrationPlanner planner;
    private readonly IMigrationPreviewer previewer;
    private readonly Func<string, string?> chooseFolder;
    private CancellationTokenSource? cancellation;
    private string sourcePath = "";
    private string destinationPath = "";
    private MigrationPreview? preview;
    private string status = "Choose source and destination folders, then generate a dry-run preview.";

    public MigrationPreviewViewModel(
        IInstanceInspector inspector,
        IMigrationPlanner planner,
        IMigrationPreviewer previewer,
        Func<string, string?> chooseFolder)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
        this.previewer = previewer ?? throw new ArgumentNullException(nameof(previewer));
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
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand BrowseSourceCommand { get; }

    public RelayCommand BrowseDestinationCommand { get; }

    public RelayCommand GeneratePreviewCommand { get; }

    public RelayCommand CancelCommand { get; }

    public bool IsBusy => cancellation is not null;

    public bool CanEditPaths => !IsBusy;

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

    public IReadOnlyList<MigrationPreviewEntry> Entries => preview?.Entries ?? [];

    public string PlanStatus => preview?.Status.ToString() ?? "Not generated";

    public string Status => status;

    public string Summary => preview is null
        ? ""
        : $"Copy: {preview.CopyCount}; Replace: {preview.ReplaceCount}; Skip: {preview.SkipCount}; " +
          $"No source: {preview.NoSourceCount}; unresolved: {preview.NeedsDecisionCount}; " +
          $"blocked: {preview.BlockedCount}; backup required: {preview.RequiresBackup}.";

    public async Task GeneratePreviewAsync()
    {
        if (!GeneratePreviewCommand.CanExecute(null))
        {
            return;
        }

        using var operation = new CancellationTokenSource();
        cancellation = operation;
        preview = null;
        status = "Inspecting source and destination metadata for dry run…";
        NotifyPreview();
        NotifyBusy();

        try
        {
            var source = await inspector.InspectAsync(SourcePath, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            var destination = await inspector.InspectAsync(DestinationPath, operation.Token);
            operation.Token.ThrowIfCancellationRequested();

            MigrationPlan plan = planner.CreateRecommendedPlan(source, destination);
            MigrationPreview generated = previewer.CreatePreview(plan);
            operation.Token.ThrowIfCancellationRequested();

            preview = generated;
            status = generated.Status switch
            {
                MigrationPlanStatus.Ready => "Dry-run preview ready. No files were changed.",
                MigrationPlanStatus.NeedsDecision => "Dry-run preview needs conflict decisions. No files were changed.",
                _ => "Dry-run preview is blocked. Review the listed states; no files were changed.",
            };
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            status = "Dry-run preview cancelled. No result retained.";
        }
        catch (Exception error)
        {
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
        preview = null;
        status = "Ready to generate a new dry-run preview. Previous preview cleared.";
        Notify(nameof(SourcePath));
        Notify(nameof(DestinationPath));
        NotifyPreview();
        GeneratePreviewCommand.Refresh();
    }

    private void NotifyPreview()
    {
        Notify(nameof(Entries));
        Notify(nameof(PlanStatus));
        Notify(nameof(Summary));
        Notify(nameof(Status));
    }

    private void NotifyBusy()
    {
        Notify(nameof(IsBusy));
        Notify(nameof(CanEditPaths));
        BrowseSourceCommand.Refresh();
        BrowseDestinationCommand.Refresh();
        GeneratePreviewCommand.Refresh();
        CancelCommand.Refresh();
    }

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
