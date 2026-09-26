using System.ComponentModel;
using System.Runtime.CompilerServices;
using MinecraftInstanceMigration.Application.Inspection;
using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.App.Presentation;

public sealed class InspectorViewModel : INotifyPropertyChanged
{
    private readonly IInstanceInspector inspector;
    private CancellationTokenSource? cancellation;
    private string candidatePath = "";
    private InstanceInspectionResult? result;
    private string status = "Choose a local folder, then select Inspect.";

    public InspectorViewModel(IInstanceInspector inspector, Func<string?> chooseFolder)
    {
        this.inspector = inspector;
        BrowseCommand = new RelayCommand(() =>
        {
            string? selected = chooseFolder();
            if (selected is not null)
            {
                CandidatePath = selected;
            }
        }, () => !IsBusy);
        InspectCommand = new RelayCommand(async () => await InspectAsync(), () => !IsBusy && !string.IsNullOrWhiteSpace(CandidatePath));
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public RelayCommand BrowseCommand { get; }
    public RelayCommand InspectCommand { get; }
    public RelayCommand CancelCommand { get; }
    public bool IsBusy => cancellation is not null;
    public bool CanEditPath => !IsBusy;

    public string CandidatePath
    {
        get => candidatePath;
        set
        {
            if (IsBusy || candidatePath == value)
            {
                return;
            }

            candidatePath = value;
            result = null;
            status = "Ready to inspect. Previous observations cleared.";
            Notify();
            NotifyResult();
            InspectCommand.Refresh();
        }
    }

    public IReadOnlyList<EntryObservation> Entries => result?.Entries ?? [];
    public string RootState => result?.RootState.ToString() ?? "Not inspected";
    public string Summary => result is null ? "" :
        $"Known entries: {result.KnownEntries}; metadata observation complete: {result.IsComplete}";
    public string Status => status;

    public async Task InspectAsync()
    {
        if (!InspectCommand.CanExecute(null))
        {
            return;
        }

        using var operation = new CancellationTokenSource();
        cancellation = operation;
        result = null;
        status = "Inspecting known direct children…";
        NotifyResult();
        NotifyBusy();
        try
        {
            var observed = await inspector.InspectAsync(CandidatePath, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            result = observed;
            status = observed.IsComplete ? "Observation finished. No files were changed." :
                "Observation incomplete. Review the root and entry states; no files were changed.";
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            status = "Inspection cancelled. No result retained.";
        }
        catch (Exception error)
        {
            // Messages and stack traces may contain private paths; expose only the failure category.
            status = $"Inspection failed ({error.GetType().Name}). No result retained.";
        }
        finally
        {
            cancellation = null;
            NotifyResult();
            NotifyBusy();
        }
    }

    public void Cancel() => cancellation?.Cancel();

    private void NotifyResult()
    {
        Notify(nameof(Entries));
        Notify(nameof(RootState));
        Notify(nameof(Summary));
        Notify(nameof(Status));
    }

    private void NotifyBusy()
    {
        Notify(nameof(IsBusy));
        Notify(nameof(CanEditPath));
        BrowseCommand.Refresh();
        InspectCommand.Refresh();
        CancelCommand.Refresh();
    }

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
