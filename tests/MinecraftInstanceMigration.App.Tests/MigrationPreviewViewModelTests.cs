using MinecraftInstanceMigration.App.Presentation;
using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Application.Inspection;
using MinecraftInstanceMigration.Application.Planning;
using MinecraftInstanceMigration.Application.Workflow;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.App.Tests;

public sealed class MigrationPreviewViewModelTests
{
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
        Assert.True(config.CanChooseConflict);
        model.SelectedEntry = config;
        Assert.True(model.ReplaceConflictCommand.CanExecute(null));
        model.ReplaceConflictCommand.Execute(null);
        model.ApplyChoicesCommand.Execute(null);

        Assert.Equal("Ready", model.PlanStatus);
        config = Entry(model, "config");
        Assert.Equal(MigrationPreviewAction.Replace, config.Action);
        Assert.Equal(MigrationPlanDisposition.ReadyToReplace, config.PlanDisposition);
        Assert.True(config.RequiresBackup);
        Assert.Equal(DestinationConflictDecision.Replace, config.ConflictDecision);
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
        Assert.Equal(DestinationConflictDecision.Skip, config.ConflictDecision);
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

        Entry(model, "config").Selected = false;
        Entry(model, "saves").Selected = true;
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
        Entry(model, "config").Selected = false;
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

    private sealed class NeverCalledExecutionOrchestrator : IExecutionOrchestrator
    {
        public Task<ExecutionOrchestrationResult> ExecuteAsync(
            ExecutionOrchestrationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Preview UI must not execute migration IO.");
    }
}
