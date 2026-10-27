using MinecraftInstanceMigration.Infrastructure.FileSystem;

namespace MinecraftInstanceMigration.Infrastructure.Tests;

// Pure lexical rules: no filesystem access, so these partitions run on every platform.
public sealed class WindowsPathRulesTests
{
    [Theory]
    [InlineData(@"C:\a", @"C:\a")]
    [InlineData(@"c:\Games\Minecraft", @"c:\Games\Minecraft")]
    [InlineData(@"D:\a\\b\", @"D:\a\b")]
    [InlineData(@"Z:\instances\hanemod-client.json", @"Z:\instances\hanemod-client.json")]
    public void AcceptsAbsoluteLocalDrivePathsAndDropsEmptyComponents(string path, string expected)
    {
        Assert.True(WindowsPathRules.TryNormalizeLexically(path, allowDriveRoot: false, out string normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:")]
    [InlineData(@"C:\")]
    [InlineData(@"1:\a")]
    [InlineData("C:/a")]
    [InlineData("C:a")]
    [InlineData(@"\\server\share\a")]
    [InlineData(@"\\?\C:\a")]
    [InlineData(@"C:\a/b")]
    [InlineData(@"C:\a\..\b")]
    [InlineData(@"C:\a\.")]
    [InlineData(@"C:\a\b.")]
    [InlineData(@"C:\a\b ")]
    [InlineData(@"C:\a:stream")]
    [InlineData(@"C:\a\b*")]
    [InlineData("C:\\a\\b\u0001")]
    public void RejectsRelativeNetworkDeviceAndAmbiguousSyntax(string? path)
    {
        Assert.False(WindowsPathRules.TryNormalizeLexically(path, allowDriveRoot: false, out string normalized));
        Assert.Equal("", normalized);
    }

    [Fact]
    public void DriveRootIsAcceptedOnlyWhenRequested()
    {
        Assert.False(WindowsPathRules.TryNormalizeLexically(@"C:\", allowDriveRoot: false, out _));
        Assert.True(WindowsPathRules.TryNormalizeLexically(@"C:\", allowDriveRoot: true, out string root));
        Assert.Equal(@"C:\", root);
    }

    [Fact]
    public void LengthBoundaryIsInclusive()
    {
        string longest = @"C:\" + new string('a', WindowsPathRules.MaximumPathLength - 3);
        string tooLong = longest + "a";

        Assert.True(WindowsPathRules.TryNormalizeLexically(longest, allowDriveRoot: false, out _));
        Assert.False(WindowsPathRules.TryNormalizeLexically(tooLong, allowDriveRoot: false, out _));
    }

    [Theory]
    [InlineData("options.txt", true)]
    [InlineData("hanemod-client.json", true)]
    [InlineData("CON", true)]
    [InlineData("", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("name.", false)]
    [InlineData("name ", false)]
    [InlineData("a<b", false)]
    [InlineData("a|b", false)]
    [InlineData("a\"b", false)]
    [InlineData("a\\b", false)]
    [InlineData("a/b", false)]
    [InlineData("a:b", false)]
    [InlineData("a\tb", false)]
    public void SingleNameRejectsTraversalSeparatorsStreamsAndControls(string name, bool expected)
    {
        // CON is a valid NT name relative to a directory handle; no DOS device translation occurs.
        Assert.Equal(expected, WindowsPathRules.IsSingleName(name));
    }

    [Theory]
    [InlineData(@"C:\a", @"C:\a", true)]
    [InlineData(@"C:\a\b", @"C:\a", true)]
    [InlineData(@"c:\A\B", @"C:\a", true)]
    [InlineData(@"C:\a", @"C:\", true)]
    [InlineData(@"C:\ab", @"C:\a", false)]
    [InlineData(@"C:\a", @"C:\a\b", false)]
    [InlineData(@"D:\a", @"C:\a", false)]
    public void DescendantCheckRespectsComponentBoundaries(string candidate, string root, bool expected)
    {
        Assert.Equal(expected, WindowsPathRules.IsEqualOrDescendant(candidate, root));
    }

    [Fact]
    public void OverlapIsSymmetric()
    {
        Assert.True(WindowsPathRules.RootsOverlap(@"C:\games", @"C:\games\new"));
        Assert.True(WindowsPathRules.RootsOverlap(@"C:\games\new", @"C:\games"));
        Assert.False(WindowsPathRules.RootsOverlap(@"C:\games\old", @"C:\games\new"));
    }
}
