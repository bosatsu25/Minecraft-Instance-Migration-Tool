using MinecraftInstanceMigration.Application.Capacity;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Infrastructure.Execution;

namespace MinecraftInstanceMigration.Infrastructure.Tests;

public sealed class WindowsMigrationCapacityProbeTests
{
    [Fact]
    public async Task DirectoryMeasurementExcludesHanemodClientFilesAtEveryDepth()
    {
        using var fixture = new InspectionFixture();
        Directory.CreateDirectory(fixture.At("config/nested"));
        await File.WriteAllBytesAsync(fixture.At("config/normal.bin"), new byte[11],
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(fixture.At("config/hanemod-client.json"), new byte[101],
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(fixture.At("config/nested/HANEMOD-CLIENT.JSON"), new byte[103],
            TestContext.Current.CancellationToken);

        LogicalSizeProbeResult result = await new WindowsMigrationCapacityProbe().MeasureAsync(
            fixture.Root,
            "config",
            ExpectedEntryKind.Directory,
            TestContext.Current.CancellationToken);

        Assert.Equal(LogicalSizeProbeStatus.Available, result.Status);
        Assert.Equal(11, result.LogicalBytes);
    }

    [Fact]
    public async Task MeasuresFileLogicalSize()
    {
        using var fixture = new InspectionFixture();
        await File.WriteAllBytesAsync(
            fixture.At("options.txt"),
            new byte[37],
            TestContext.Current.CancellationToken);
        var subject = new WindowsMigrationCapacityProbe();

        LogicalSizeProbeResult result = await subject.MeasureAsync(
            fixture.Root,
            "options.txt",
            ExpectedEntryKind.File,
            TestContext.Current.CancellationToken);

        Assert.Equal(LogicalSizeProbeStatus.Available, result.Status);
        Assert.Equal(37, result.LogicalBytes);
    }

    [Fact]
    public async Task MeasuresNestedDirectoryIncludingZeroByteFiles()
    {
        using var fixture = new InspectionFixture();
        Directory.CreateDirectory(fixture.At("config\\nested"));
        await File.WriteAllBytesAsync(fixture.At("config\\a.bin"), new byte[10],
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(fixture.At("config\\nested\\b.bin"), new byte[20],
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(fixture.At("config\\empty.bin"), [],
            TestContext.Current.CancellationToken);
        var subject = new WindowsMigrationCapacityProbe();

        LogicalSizeProbeResult result = await subject.MeasureAsync(
            fixture.Root,
            "config",
            ExpectedEntryKind.Directory,
            TestContext.Current.CancellationToken);

        Assert.Equal(LogicalSizeProbeStatus.Available, result.Status);
        Assert.Equal(30, result.LogicalBytes);
    }

    [Fact]
    public async Task MissingEntryIsUnavailable()
    {
        using var fixture = new InspectionFixture();
        var subject = new WindowsMigrationCapacityProbe();

        LogicalSizeProbeResult result = await subject.MeasureAsync(
            fixture.Root,
            "missing",
            ExpectedEntryKind.Directory,
            TestContext.Current.CancellationToken);

        Assert.NotEqual(LogicalSizeProbeStatus.Available, result.Status);
        Assert.Null(result.LogicalBytes);
    }

    [Fact]
    public async Task NestedJunctionIsRejectedWithoutFollowingTarget()
    {
        using var fixture = new InspectionFixture();
        Directory.CreateDirectory(fixture.At("config"));
        Directory.CreateDirectory(fixture.At("target"));
        await File.WriteAllBytesAsync(fixture.At("target\\secret.bin"), new byte[99],
            TestContext.Current.CancellationToken);
        fixture.Junction("config\\link", "target");
        var subject = new WindowsMigrationCapacityProbe();

        LogicalSizeProbeResult result = await subject.MeasureAsync(
            fixture.Root,
            "config",
            ExpectedEntryKind.Directory,
            TestContext.Current.CancellationToken);

        Assert.Equal(LogicalSizeProbeStatus.ReparsePoint, result.Status);
        Assert.Null(result.LogicalBytes);
    }

    [Fact]
    public async Task ProbesCanonicalVolumeIdentityAndAvailableBytes()
    {
        using var fixture = new InspectionFixture();
        var subject = new WindowsMigrationCapacityProbe();

        VolumeCapacityProbeResult first = await subject.ProbeAsync(
            fixture.Root,
            TestContext.Current.CancellationToken);
        VolumeCapacityProbeResult second = await subject.ProbeAsync(
            Directory.CreateDirectory(fixture.At("workspace")).FullName,
            TestContext.Current.CancellationToken);

        Assert.Equal(VolumeCapacityProbeStatus.Available, first.Status);
        Assert.False(string.IsNullOrWhiteSpace(first.VolumeIdentity));
        Assert.True(first.AvailableBytes > 0);
        Assert.Equal(first.VolumeIdentity, second.VolumeIdentity);
    }

    [Fact]
    public async Task InvalidOrReparseRootIsNeverReportedAvailable()
    {
        using var fixture = new InspectionFixture();
        Directory.CreateDirectory(fixture.At("target"));
        fixture.Junction("alias", "target");
        var subject = new WindowsMigrationCapacityProbe();

        VolumeCapacityProbeResult invalid = await subject.ProbeAsync(
            "relative",
            TestContext.Current.CancellationToken);
        VolumeCapacityProbeResult reparse = await subject.ProbeAsync(
            fixture.At("alias"),
            TestContext.Current.CancellationToken);

        Assert.Equal(VolumeCapacityProbeStatus.InvalidPath, invalid.Status);
        Assert.Equal(VolumeCapacityProbeStatus.ReparsePoint, reparse.Status);
    }
}
