using MinecraftInstanceMigration.Application.Inspection;
using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Application.Tests;

public sealed class InstanceInspectorTests
{
    [Fact]
    public async Task ObservesExactlyTheKnownDirectChildrenInStableOrder()
    {
        var fileSystem = new StubFileSystem();
        var result = await new InstanceInspector(fileSystem).InspectAsync("candidate", TestContext.Current.CancellationToken);
        Assert.Equal(new[]
        {
            "options.txt", "config", "resourcepacks", "shaderpacks", "schematics", "saves",
            "screenshots", "XaeroWaypoints", "XaeroWorldMap", "itemscroller", "g4mespeed",
        }, result.Entries.Select(entry => entry.Name));
        Assert.Equal(result.Entries.Select(entry => entry.Name), fileSystem.Session.Requests);
        Assert.Equal(ExpectedEntryKind.File, result.Entries[0].ExpectedKind);
        Assert.All(result.Entries.Skip(1), entry => Assert.Equal(ExpectedEntryKind.Directory, entry.ExpectedKind));
        Assert.Equal(KnownEntriesState.NoneObserved, result.KnownEntries);
        Assert.True(fileSystem.Session.Disposed);
    }

    [Theory]
    [InlineData(EntryState.Missing)]
    [InlineData(EntryState.File)]
    [InlineData(EntryState.Inaccessible)]
    [InlineData(EntryState.ReparsePoint)]
    [InlineData(EntryState.InvalidPath)]
    [InlineData(EntryState.Unavailable)]
    public async Task InvalidOrBlockedRootNeverRequestsChildren(EntryState rootState)
    {
        var fileSystem = new StubFileSystem(rootState);
        var result = await new InstanceInspector(fileSystem).InspectAsync("candidate", TestContext.Current.CancellationToken);
        Assert.Equal(rootState, result.RootState);
        Assert.Empty(result.Entries);
        Assert.Empty(fileSystem.Session.Requests);
        Assert.True(fileSystem.Session.Disposed);
    }

    [Fact]
    public async Task KeepsOtherObservationsWhenOneChildIsInaccessible()
    {
        var fileSystem = new StubFileSystem();
        fileSystem.Session.Observe = name => name switch
        {
            "config" => EntryState.Inaccessible,
            "saves" => EntryState.Directory,
            _ => EntryState.Missing,
        };
        var result = await new InstanceInspector(fileSystem).InspectAsync("candidate", TestContext.Current.CancellationToken);
        Assert.Equal(EntryState.Inaccessible, result.Entries.Single(entry => entry.Name == "config").State);
        Assert.Equal(EntryState.Directory, result.Entries.Single(entry => entry.Name == "saves").State);
        Assert.Equal(11, result.Entries.Count);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task PreCancellationDoesNotOpenTheFileSystem()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var fileSystem = new StubFileSystem();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new InstanceInspector(fileSystem).InspectAsync("candidate", cancellation.Token));
        Assert.False(fileSystem.Opened);
    }

    [Fact]
    public async Task MidInspectionCancellationStopsAndDisposesSession()
    {
        using var cancellation = new CancellationTokenSource();
        var fileSystem = new StubFileSystem();
        fileSystem.Session.Observe = _ =>
        {
            cancellation.Cancel();
            return EntryState.File;
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new InstanceInspector(fileSystem).InspectAsync("candidate", cancellation.Token));
        Assert.Single(fileSystem.Session.Requests);
        Assert.True(fileSystem.Session.Disposed);
    }

    [Fact]
    public async Task UnexpectedFailureRemainsDiagnosableAndDisposesSession()
    {
        var fileSystem = new StubFileSystem();
        fileSystem.Session.Observe = _ => throw new InvalidOperationException("Programming failure");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InstanceInspector(fileSystem).InspectAsync("candidate", TestContext.Current.CancellationToken));
        Assert.True(fileSystem.Session.Disposed);
    }

    private sealed class StubFileSystem(EntryState state = EntryState.Directory) : IInspectionFileSystem
    {
        public bool Opened { get; private set; }
        public StubSession Session { get; } = new(state);

        public IInspectionSession OpenRoot(string candidatePath, CancellationToken cancellationToken)
        {
            Opened = true;
            return Session;
        }
    }

    private sealed class StubSession(EntryState state) : IInspectionSession
    {
        public EntryState RootState { get; } = state;
        public List<string> Requests { get; } = [];
        public Func<string, EntryState> Observe { get; set; } = _ => EntryState.Missing;
        public bool Disposed { get; private set; }

        public EntryState ObserveChild(string childName, CancellationToken cancellationToken)
        {
            Requests.Add(childName);
            return Observe(childName);
        }

        public void Dispose() => Disposed = true;
    }
}
