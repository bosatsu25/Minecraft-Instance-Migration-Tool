using MinecraftInstanceMigration.App.Presentation;
using MinecraftInstanceMigration.Application.Inspection;
using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.App.Tests;

public sealed class InspectorViewModelTests
{
    [Fact]
    public async Task ChangingInputClearsOldResultsAndBrowseCancelPreservesInput()
    {
        var inspector = new StubInspector((_, _) => Task.FromResult(new InstanceInspectionResult(EntryState.Directory, [])));
        var model = new InspectorViewModel(inspector, () => null) { CandidatePath = "first" };
        await model.InspectAsync();
        Assert.Equal("Directory", model.RootState);
        model.BrowseCommand.Execute(null);
        Assert.Equal("first", model.CandidatePath);
        model.CandidatePath = "second";
        Assert.Equal("Not inspected", model.RootState);
        Assert.Empty(model.Entries);
    }

    [Fact]
    public async Task BusyOperationDisablesEditingAndCancelsWithoutPublishingLateResult()
    {
        var pending = new TaskCompletionSource<InstanceInspectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken received = default;
        int calls = 0;
        var model = new InspectorViewModel(new StubInspector((_, token) =>
        {
            calls++;
            received = token;
            return pending.Task;
        }), () => "other")
        { CandidatePath = "first" };
        Task running = model.InspectAsync();
        Assert.True(model.IsBusy);
        Assert.False(model.BrowseCommand.CanExecute(null));
        Assert.False(model.InspectCommand.CanExecute(null));
        model.CandidatePath = "second";
        Assert.Equal("first", model.CandidatePath);
        await model.InspectAsync();
        Assert.Equal(1, calls);
        model.CancelCommand.Execute(null);
        Assert.True(received.IsCancellationRequested);
        pending.SetResult(new InstanceInspectionResult(EntryState.Directory, []));
        await running;
        Assert.False(model.IsBusy);
        Assert.Equal("Not inspected", model.RootState);
        Assert.Contains("cancelled", model.Status);
    }

    [Fact]
    public async Task ErrorIsVisibleWithoutLeakingExceptionMessageAndCanRetry()
    {
        var model = new InspectorViewModel(new StubInspector((_, _) =>
            throw new InvalidOperationException("private candidate path")), () => null)
        { CandidatePath = "first" };
        await model.InspectAsync();
        Assert.Contains("InvalidOperationException", model.Status);
        Assert.DoesNotContain("private", model.Status);
        Assert.False(model.IsBusy);
        Assert.True(model.InspectCommand.CanExecute(null));
        Assert.Empty(model.Entries);
    }

    [Fact]
    public void EmptyInputCannotStartAndBrowseUsesSelectedPath()
    {
        var model = new InspectorViewModel(new StubInspector((_, _) => throw new InvalidOperationException()), () => "chosen");
        Assert.False(model.InspectCommand.CanExecute(null));
        model.BrowseCommand.Execute(null);
        Assert.Equal("chosen", model.CandidatePath);
        Assert.True(model.InspectCommand.CanExecute(null));
    }

    private sealed class StubInspector(Func<string, CancellationToken, Task<InstanceInspectionResult>> run) : IInstanceInspector
    {
        public Task<InstanceInspectionResult> InspectAsync(string candidatePath, CancellationToken cancellationToken = default) =>
            run(candidatePath, cancellationToken);
    }
}
