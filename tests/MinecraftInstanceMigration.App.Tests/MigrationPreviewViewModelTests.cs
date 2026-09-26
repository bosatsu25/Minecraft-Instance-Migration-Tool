using MinecraftInstanceMigration.App.Presentation;
using MinecraftInstanceMigration.Application.Inspection;
using MinecraftInstanceMigration.Application.Planning;
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
    }

    [Fact]
    public async Task ExistingDestinationIsVisibleAsNeedsDecision()
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
        Assert.Equal(MigrationPreviewAction.NeedsDecision, Entry(model, "config").Action);
        Assert.Contains("needs conflict decisions", model.Status);
    }

    [Fact]
    public async Task ChangingInputClearsPreviousPreview()
    {
        var model = CreateModel(new StubInspector((_, _) =>
            Task.FromResult(Inspection(("config", EntryState.Directory)))));
        model.SourcePath = "source";
        model.DestinationPath = "destination";

        await model.GeneratePreviewAsync();
        Assert.NotEmpty(model.Entries);

        model.DestinationPath = "new-destination";

        Assert.Empty(model.Entries);
        Assert.Equal("Not generated", model.PlanStatus);
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
    public async Task ErrorCategoryIsShownWithoutLeakingPrivateMessage()
    {
        var model = CreateModel(new StubInspector((_, _) =>
            throw new InvalidOperationException("private source path")));
        model.SourcePath = "source";
        model.DestinationPath = "destination";

        await model.GeneratePreviewAsync();

        Assert.Contains("InvalidOperationException", model.Status);
        Assert.DoesNotContain("private", model.Status);
        Assert.Empty(model.Entries);
    }

    [Fact]
    public void BrowseCommandsUsePurposeSpecificTitles()
    {
        var titles = new List<string>();
        var model = new MigrationPreviewViewModel(
            new StubInspector((_, _) => throw new InvalidOperationException()),
            new MigrationPlanner(),
            new MigrationPreviewer(),
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
        new(inspector, new MigrationPlanner(), new MigrationPreviewer(), _ => null);

    private static MigrationPreviewEntry Entry(MigrationPreviewViewModel model, string name) =>
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

    private sealed class StubInspector(Func<string, CancellationToken, Task<InstanceInspectionResult>> run) : IInstanceInspector
    {
        public Task<InstanceInspectionResult> InspectAsync(
            string candidatePath,
            CancellationToken cancellationToken = default) =>
            run(candidatePath, cancellationToken);
    }
}
