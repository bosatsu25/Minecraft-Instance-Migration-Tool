using MinecraftInstanceMigration.Application.Capacity;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Tests;

public sealed class MigrationCapacityPreflightTests
{
    [Fact]
    public async Task CopyAndReplaceUseSourceBytesAndReplacementUsesDestinationBackupBytes()
    {
        var sizes = new StubSizeProbe(new Dictionary<(string, string), long>
        {
            [("source", "options.txt")] = 10,
            [("source", "config")] = 20,
            [("destination", "config")] = 30,
        });
        var subject = new MigrationCapacityPreflight(sizes, Volumes(100_000_000, 100_000_000));

        MigrationCapacityEstimate result = await subject.EvaluateAsync(
            Request(Plan(Copy("options.txt"), Replace("config"))),
            TestContext.Current.CancellationToken);

        Assert.Equal(MigrationCapacityStatus.Ready, result.Status);
        Assert.Equal(10, result.CopyBytes);
        Assert.Equal(20, result.ReplaceWriteBytes);
        Assert.Equal(30, result.BackupBytes);
    }

    [Fact]
    public async Task SkippedAndExcludedEntriesAreNotMeasured()
    {
        var sizes = new StubSizeProbe(new Dictionary<(string Root, string Name), long>());
        var subject = new MigrationCapacityPreflight(sizes, Volumes(100_000_000, 100_000_000));

        MigrationCapacityEstimate result = await subject.EvaluateAsync(
            Request(Plan(
                Entry("saves", MigrationPlanDisposition.ExcludedBySelection),
                Entry("config", MigrationPlanDisposition.SkippedDestinationConflict))),
            TestContext.Current.CancellationToken);

        Assert.Equal(MigrationCapacityStatus.Ready, result.Status);
        Assert.Equal(0, result.CopyBytes);
        Assert.Empty(sizes.Calls);
    }

    [Fact]
    public async Task InsufficientDestinationSpaceBlocks()
    {
        var subject = new MigrationCapacityPreflight(
            Sizes(("source", "options.txt", 10)),
            Volumes(1, 100_000_000));

        MigrationCapacityEstimate result = await subject.EvaluateAsync(
            Request(Plan(Copy("options.txt"))),
            TestContext.Current.CancellationToken);

        Assert.Equal(MigrationCapacityStatus.InsufficientDestinationSpace, result.Status);
    }

    [Fact]
    public async Task InsufficientWorkspaceSpaceBlocks()
    {
        var subject = new MigrationCapacityPreflight(
            Sizes(("source", "config", 10), ("destination", "config", 20)),
            Volumes(100_000_000, 1));

        MigrationCapacityEstimate result = await subject.EvaluateAsync(
            Request(Plan(Replace("config"))),
            TestContext.Current.CancellationToken);

        Assert.Equal(MigrationCapacityStatus.InsufficientWorkspaceSpace, result.Status);
    }

    [Fact]
    public async Task SameVolumeCombinesWriteAndBackupRequirement()
    {
        var subject = new MigrationCapacityPreflight(
            Sizes(("source", "config", 50_000_000), ("destination", "config", 50_000_000)),
            Volumes(120_000_000, 120_000_000, sameVolume: true));

        MigrationCapacityEstimate result = await subject.EvaluateAsync(
            Request(Plan(Replace("config"))),
            TestContext.Current.CancellationToken);

        Assert.True(result.SharesVolume);
        Assert.NotNull(result.SharedVolumeRequiredBytes);
        Assert.Equal(MigrationCapacityStatus.InsufficientSharedVolumeSpace, result.Status);
    }

    [Fact]
    public async Task SeparateVolumesAreComparedIndependently()
    {
        var subject = new MigrationCapacityPreflight(
            Sizes(("source", "config", 50_000_000), ("destination", "config", 50_000_000)),
            Volumes(120_000_000, 120_000_000));

        MigrationCapacityEstimate result = await subject.EvaluateAsync(
            Request(Plan(Replace("config"))),
            TestContext.Current.CancellationToken);

        Assert.False(result.SharesVolume);
        Assert.Equal(MigrationCapacityStatus.Ready, result.Status);
    }

    [Theory]
    [InlineData(LogicalSizeProbeStatus.Missing, MigrationCapacityFailureKind.MeasurementUnavailable)]
    [InlineData(LogicalSizeProbeStatus.AccessDenied, MigrationCapacityFailureKind.MeasurementUnavailable)]
    [InlineData(LogicalSizeProbeStatus.ReparsePoint, MigrationCapacityFailureKind.UnsafeTree)]
    public async Task MeasurementFailureNeverReturnsReady(
        LogicalSizeProbeStatus status,
        MigrationCapacityFailureKind expectedFailure)
    {
        var subject = new MigrationCapacityPreflight(
            new StubSizeProbe(
                new Dictionary<(string Root, string Name), long>(),
                new LogicalSizeProbeResult(status)),
            Volumes(100_000_000, 100_000_000));

        MigrationCapacityEstimate result = await subject.EvaluateAsync(
            Request(Plan(Copy("options.txt"))),
            TestContext.Current.CancellationToken);

        Assert.Equal(MigrationCapacityStatus.Unavailable, result.Status);
        Assert.Equal(expectedFailure, result.FailureKind);
    }

    [Fact]
    public async Task VolumeFailureNeverReturnsReady()
    {
        var subject = new MigrationCapacityPreflight(
            Sizes(("source", "options.txt", 10)),
            new StubVolumeProbe(new Dictionary<string, VolumeCapacityProbeResult>
            {
                ["destination"] = new(VolumeCapacityProbeStatus.Unavailable),
                ["workspace"] = new(VolumeCapacityProbeStatus.Available, "workspace-volume", 100_000_000),
            }));

        MigrationCapacityEstimate result = await subject.EvaluateAsync(
            Request(Plan(Copy("options.txt"))),
            TestContext.Current.CancellationToken);

        Assert.Equal(MigrationCapacityStatus.Unavailable, result.Status);
        Assert.Equal(MigrationCapacityFailureKind.VolumeUnavailable, result.FailureKind);
    }

    [Fact]
    public async Task NegativeMeasurementNeverReturnsReady()
    {
        var subject = new MigrationCapacityPreflight(
            Sizes(("source", "options.txt", -1)),
            Volumes(100_000_000, 100_000_000));

        MigrationCapacityEstimate result = await subject.EvaluateAsync(
            Request(Plan(Copy("options.txt"))),
            TestContext.Current.CancellationToken);

        Assert.Equal(MigrationCapacityStatus.Unavailable, result.Status);
        Assert.Equal(MigrationCapacityFailureKind.InvalidMeasurement, result.FailureKind);
    }

    [Fact]
    public async Task ArithmeticOverflowNeverReturnsReady()
    {
        var subject = new MigrationCapacityPreflight(
            Sizes(
                ("source", "options.txt", long.MaxValue),
                ("source", "other.txt", 1)),
            Volumes(long.MaxValue, long.MaxValue));

        MigrationCapacityEstimate result = await subject.EvaluateAsync(
            Request(Plan(Copy("options.txt"), Copy("other.txt"))),
            TestContext.Current.CancellationToken);

        Assert.Equal(MigrationCapacityStatus.Unavailable, result.Status);
        Assert.Equal(MigrationCapacityFailureKind.ArithmeticOverflow, result.FailureKind);
    }

    [Fact]
    public async Task CancellationNeverPublishesPartialReadyEstimate()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var subject = new MigrationCapacityPreflight(
            Sizes(("source", "options.txt", 10)),
            Volumes(100_000_000, 100_000_000));

        MigrationCapacityEstimate result = await subject.EvaluateAsync(
            Request(Plan(Copy("options.txt"))),
            cancellation.Token);

        Assert.Equal(MigrationCapacityStatus.Cancelled, result.Status);
        Assert.Equal(MigrationCapacityFailureKind.Cancelled, result.FailureKind);
    }

    [Fact]
    public void MarginIsBoundedAndChecked()
    {
        Assert.Equal(64L * 1024 * 1024, MigrationCapacityMarginPolicy.RequiredBytes(0));
        Assert.Equal(1_088L * 1024 * 1024,
            MigrationCapacityMarginPolicy.RequiredBytes(1024L * 1024 * 1024));
        Assert.Throws<OverflowException>(() => MigrationCapacityMarginPolicy.RequiredBytes(long.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => MigrationCapacityMarginPolicy.RequiredBytes(-1));
    }

    private static MigrationCapacityRequest Request(MigrationPlan plan) =>
        new("source", "destination", "workspace", plan);

    private static MigrationPlan Plan(params MigrationPlanEntry[] entries) =>
        new(EntryState.Directory, EntryState.Directory, entries, []);

    private static MigrationPlanEntry Copy(string name) =>
        Entry(name, MigrationPlanDisposition.ReadyToCopy, EntryState.Missing);

    private static MigrationPlanEntry Replace(string name) =>
        Entry(name, MigrationPlanDisposition.ReadyToReplace, EntryState.Directory);

    private static MigrationPlanEntry Entry(
        string name,
        MigrationPlanDisposition disposition,
        EntryState destination = EntryState.Missing) =>
        new(name, name.EndsWith(".txt", StringComparison.Ordinal)
                ? ExpectedEntryKind.File
                : ExpectedEntryKind.Directory,
            EntryState.Directory,
            destination,
            disposition != MigrationPlanDisposition.ExcludedBySelection,
            disposition);

    private static StubSizeProbe Sizes(params (string Root, string Name, long Bytes)[] sizes) =>
        new(sizes.ToDictionary(item => (item.Root, item.Name), item => item.Bytes));

    private static StubVolumeProbe Volumes(long destination, long workspace, bool sameVolume = false) =>
        new(new Dictionary<string, VolumeCapacityProbeResult>
        {
            ["destination"] = new(VolumeCapacityProbeStatus.Available, "destination-volume", destination),
            ["workspace"] = new(VolumeCapacityProbeStatus.Available,
                sameVolume ? "destination-volume" : "workspace-volume", workspace),
        });

    private sealed class StubSizeProbe(
        IReadOnlyDictionary<(string Root, string Name), long> sizes,
        LogicalSizeProbeResult? missing = null) : ILogicalSizeProbe
    {
        public List<(string Root, string Name)> Calls { get; } = [];

        public Task<LogicalSizeProbeResult> MeasureAsync(
            string root,
            string entryName,
            ExpectedEntryKind expectedKind,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add((root, entryName));
            return Task.FromResult(sizes.TryGetValue((root, entryName), out long bytes)
                ? new LogicalSizeProbeResult(LogicalSizeProbeStatus.Available, bytes)
                : missing ?? new LogicalSizeProbeResult(LogicalSizeProbeStatus.Missing));
        }
    }

    private sealed class StubVolumeProbe(
        IReadOnlyDictionary<string, VolumeCapacityProbeResult> results) : IVolumeCapacityProbe
    {
        public Task<VolumeCapacityProbeResult> ProbeAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(results[path]);
        }
    }
}
