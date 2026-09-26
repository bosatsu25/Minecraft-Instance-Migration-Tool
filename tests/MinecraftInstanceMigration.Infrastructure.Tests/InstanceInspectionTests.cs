using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using MinecraftInstanceMigration.Application.Inspection;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Infrastructure.Inspection;

namespace MinecraftInstanceMigration.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public sealed class InstanceInspectionTests
{
    private static Task<InstanceInspectionResult> Inspect(string root) =>
        new InstanceInspector(new WindowsInspectionFileSystem()).InspectAsync(root, TestContext.Current.CancellationToken);

    [Fact]
    public async Task MissingRootStaysMissingAndIsNotCreated()
    {
        using var fixture = new InspectionFixture();
        var result = await Inspect(fixture.At("missing"));
        Assert.Equal(EntryState.Missing, result.RootState);
        Assert.Empty(result.Entries);
        Assert.False(Directory.Exists(fixture.At("missing")));
    }

    [Fact]
    public async Task FileRootIsDistinguishedFromMissingRoot()
    {
        using var fixture = new InspectionFixture();
        File.WriteAllText(fixture.At("root-file"), "fixture");
        var result = await Inspect(fixture.At("root-file"));
        Assert.Equal(EntryState.File, result.RootState);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task EmptyArbitrarilyNamedDirectoryHasNoKnownMarkers()
    {
        using var fixture = new InspectionFixture();
        var result = await Inspect(fixture.Root);
        Assert.Equal(EntryState.Directory, result.RootState);
        Assert.Equal(KnownEntriesState.NoneObserved, result.KnownEntries);
        Assert.Equal(11, result.Entries.Count);
        Assert.All(result.Entries, entry => Assert.Equal(EntryState.Missing, entry.State));
    }

    [Theory]
    [InlineData("options.txt", false, EntryState.File, true)]
    [InlineData("config", true, EntryState.Directory, true)]
    [InlineData("config", false, EntryState.File, false)]
    [InlineData("options.txt", true, EntryState.Directory, false)]
    public async Task ReportsActualTypeAndExpectedTypeSeparately(
        string name, bool directory, EntryState state, bool matches)
    {
        using var fixture = new InspectionFixture();
        if (directory)
        {
            Directory.CreateDirectory(fixture.At(name));
        }
        else
        {
            File.WriteAllText(fixture.At(name), "fixture");
        }

        var result = await Inspect(fixture.Root);
        var observation = Assert.Single(result.Entries, entry => entry.Name == name);
        Assert.Equal(state, observation.State);
        Assert.Equal(matches, observation.MatchesExpectedKind);
        Assert.Equal(KnownEntriesState.Present, result.KnownEntries);
    }

    [Fact]
    public async Task ReadsOnlyKnownMetadataAndChangesNothing()
    {
        using var fixture = new InspectionFixture();
        File.WriteAllText(fixture.At("options.txt"), "fixture-options");
        Directory.CreateDirectory(fixture.At("config/nested"));
        File.WriteAllText(fixture.At("config/nested/settings.json"), "fixture-data");
        Directory.CreateDirectory(fixture.At("saves/world"));
        File.WriteAllText(fixture.At("saves/world/level.dat"), "fixture-world");
        Directory.CreateDirectory(fixture.At("unknown-directory"));
        File.WriteAllText(fixture.At("unknown.txt"), "fixture-unknown");
        var before = fixture.Snapshot();

        var result = await Inspect(fixture.Root);

        Assert.Equal(before, fixture.Snapshot());
        Assert.Equal(11, result.Entries.Count);
        Assert.Equal(3, result.Entries.Count(entry => entry.State is EntryState.File or EntryState.Directory));
        Assert.DoesNotContain(result.Entries, entry => entry.Name.StartsWith("unknown", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DoesNotNeedAccessToNestedContents()
    {
        using var fixture = new InspectionFixture();
        Directory.CreateDirectory(fixture.At("config"));
        File.WriteAllText(fixture.At("config/locked"), "fixture");
        using var locked = new FileStream(fixture.At("config/locked"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = await Inspect(fixture.Root);
        Assert.Equal(EntryState.Directory, result.Entries.Single(entry => entry.Name == "config").State);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task RootJunctionIsReportedWithoutInspectingItsTarget()
    {
        using var fixture = new InspectionFixture();
        Directory.CreateDirectory(fixture.At("target/config"));
        fixture.Junction("linked-root", "target");
        var result = await Inspect(fixture.At("linked-root"));
        Assert.Equal(EntryState.ReparsePoint, result.RootState);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task AncestorJunctionBlocksInspectionEvenWhenRootItselfIsNormal()
    {
        using var fixture = new InspectionFixture();
        Directory.CreateDirectory(fixture.At("target/instance/config"));
        fixture.Junction("linked-parent", "target");
        var result = await Inspect(fixture.At("linked-parent/instance"));
        Assert.Equal(EntryState.ReparsePoint, result.RootState);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task KnownChildJunctionPreservesLinkObservationWithoutFollowing()
    {
        using var fixture = new InspectionFixture();
        Directory.CreateDirectory(fixture.At("instance"));
        Directory.CreateDirectory(fixture.At("target"));
        fixture.Junction("instance/config", "target");
        var result = await Inspect(fixture.At("instance"));
        Assert.Equal(EntryState.ReparsePoint, result.Entries.Single(entry => entry.Name == "config").State);
        Assert.Equal(11, result.Entries.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/path")]
    [InlineData("C:relative")]
    [InlineData(@"\\server\share\instance")]
    [InlineData(@"\\?\C:\instance")]
    [InlineData(@"C:\instance\..\outside")]
    [InlineData(@"C:\instance:stream")]
    [InlineData(@"C:\instance.\config")]
    public async Task RejectsUnsafeOrNonLocalInputsBeforeInspection(string input)
    {
        var result = await Inspect(input);
        Assert.Equal(EntryState.InvalidPath, result.RootState);
        Assert.Empty(result.Entries);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("config/child")]
    [InlineData(@"C:\outside")]
    [InlineData("config:stream")]
    public void SessionRejectsPathsThatAreNotSingleChildNames(string name)
    {
        using var fixture = new InspectionFixture();
        using var session = new WindowsInspectionFileSystem().OpenRoot(fixture.Root, TestContext.Current.CancellationToken);
        Assert.Equal(EntryState.InvalidPath, session.ObserveChild(name, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeniedMetadataAccessIsNotMisreportedAsMissing(bool denyChild)
    {
        using var fixture = new InspectionFixture();
        Directory.CreateDirectory(fixture.At("instance/config"));
        var directory = new DirectoryInfo(fixture.At(denyChild ? "instance/config" : "instance"));
        var original = directory.GetAccessControl();
        var denied = directory.GetAccessControl();
        var parent = directory.Parent!;
        var parentOriginal = parent.GetAccessControl();
        var parentDenied = parent.GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        denied.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ReadAttributes, AccessControlType.Deny));
        // Windows also permits attribute reads through the containing directory's listing right.
        parentDenied.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ListDirectory, AccessControlType.Deny));
        try
        {
            parent.SetAccessControl(parentDenied);
            directory.SetAccessControl(denied);
            var result = await Inspect(fixture.At("instance"));
            if (denyChild)
            {
                Assert.Equal(EntryState.Directory, result.RootState);
                Assert.Equal(EntryState.Inaccessible, result.Entries.Single(entry => entry.Name == "config").State);
                Assert.Equal(KnownEntriesState.Indeterminate, result.KnownEntries);
                Assert.False(result.IsComplete);
            }
            else
            {
                Assert.Equal(EntryState.Inaccessible, result.RootState);
                Assert.Empty(result.Entries);
            }
        }
        finally
        {
            parentDenied.SetSecurityDescriptorBinaryForm(parentOriginal.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
            parent.SetAccessControl(parentDenied);
            denied.SetSecurityDescriptorBinaryForm(original.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
            directory.SetAccessControl(denied);
        }
    }

    [Fact]
    public async Task ExistingDeleteAccessDoesNotPreventMetadataObservation()
    {
        using var fixture = new InspectionFixture();
        Directory.CreateDirectory(fixture.At("instance"));
        using var conflictingHandle = fixture.HoldRenameAccess("instance");
        var result = await Inspect(fixture.At("instance"));
        Assert.Equal(EntryState.Directory, result.RootState);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void RootConvertedToJunctionDuringSessionNeverExposesTargetChildren()
    {
        using var fixture = new InspectionFixture();
        Directory.CreateDirectory(fixture.At("instance"));
        Directory.CreateDirectory(fixture.At("target/config"));
        using var session = new WindowsInspectionFileSystem().OpenRoot(fixture.At("instance"), TestContext.Current.CancellationToken);
        Assert.Equal(EntryState.Directory, session.RootState);
        fixture.ConvertDirectoryToJunction("instance", "target");
        var child = session.ObserveChild("config", TestContext.Current.CancellationToken);
        // Windows returns STATUS_REPARSE_POINT_NOT_RESOLVED: unknown, never absent or target metadata.
        Assert.Equal(EntryState.Unavailable, child);
    }

    [Fact]
    public void AncestorCannotBeReplacedUntilSessionIsDisposed()
    {
        using var fixture = new InspectionFixture();
        Directory.CreateDirectory(fixture.At("parent/instance"));
        using (var session = new WindowsInspectionFileSystem().OpenRoot(fixture.At("parent/instance"), TestContext.Current.CancellationToken))
        {
            Assert.Equal(EntryState.Directory, session.RootState);
            Assert.Throws<IOException>(() => Directory.Move(fixture.At("parent"), fixture.At("renamed")));
        }

        Directory.Move(fixture.At("parent"), fixture.At("renamed"));
        Assert.True(Directory.Exists(fixture.At("renamed/instance")));
    }
}
