using System.Buffers.Binary;
using System.Xml.Linq;

namespace MinecraftInstanceMigration.App.Tests;

public sealed class ReleaseConfigurationTests
{
    [Fact]
    public void CentralMetadataDefinesTheDevelopmentVersionOnce()
    {
        XDocument props = XDocument.Load(FindRepositoryFile("Directory.Build.props"));

        Assert.Equal("0.9.0", SingleValue(props, "Version"));
        Assert.Equal("0.9.0.0", SingleValue(props, "AssemblyVersion"));
        Assert.Equal("0.9.0.0", SingleValue(props, "FileVersion"));
        Assert.Equal("0.9.0", SingleValue(props, "InformationalVersion"));
        Assert.Equal("false", SingleValue(props, "IncludeSourceRevisionInInformationalVersion"));
        Assert.Equal("Minecraft Instance Migration Tool", SingleValue(props, "Product"));
        Assert.Equal("bosatsuKing", SingleValue(props, "Authors"));
    }

    [Fact]
    public void PublishProfileIsExplicitlySelfContainedAndUntrimmedWinX64()
    {
        XDocument profile = XDocument.Load(FindRepositoryFile(
            "src", "MinecraftInstanceMigration.App", "Properties", "PublishProfiles", "WinX64.pubxml"));

        Assert.Equal("win-x64", SingleValue(profile, "RuntimeIdentifier"));
        Assert.Equal("true", SingleValue(profile, "SelfContained"));
        Assert.Equal("false", SingleValue(profile, "PublishSingleFile"));
        Assert.Equal("false", SingleValue(profile, "PublishTrimmed"));
        Assert.Equal("false", SingleValue(profile, "PublishAot"));
        Assert.Equal("x64", SingleValue(profile, "PlatformTarget"));
        Assert.Equal("none", SingleValue(profile, "DebugType"));
        Assert.Equal("false", SingleValue(profile, "DebugSymbols"));
    }

    [Fact]
    public void InstallerIsPerUserX64AndKeepsUserDataOutsideItsOwnership()
    {
        string installer = File.ReadAllText(FindRepositoryFile("eng", "release", "installer.iss"));

        Assert.Contains("PrivilegesRequired=lowest", installer, StringComparison.Ordinal);
        Assert.Contains("DefaultDirName={localappdata}\\Programs", installer, StringComparison.Ordinal);
        Assert.Contains("ArchitecturesAllowed=x64compatible", installer, StringComparison.Ordinal);
        Assert.Contains("ArchitecturesInstallIn64BitMode=x64compatible", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("[UninstallDelete]", installer, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationAndInstallerUseTheSameMultiResolutionIcon()
    {
        string projectPath = FindRepositoryFile(
            "src", "MinecraftInstanceMigration.App", "MinecraftInstanceMigration.App.csproj");
        XDocument project = XDocument.Load(projectPath);
        Assert.Equal("Assets\\AppIcon.ico", SingleValue(project, "ApplicationIcon"));
        Assert.Contains(project.Descendants("Resource"), element =>
            (string?)element.Attribute("Include") == "Assets\\AppIcon.ico");
        Assert.Contains(project.Descendants("None"), element =>
            (string?)element.Attribute("Update") == "Assets\\AppIcon.png" &&
            (string?)element.Attribute("CopyToPublishDirectory") == "Never");

        string iconPath = FindRepositoryFile("src", "MinecraftInstanceMigration.App", "Assets", "AppIcon.ico");
        Assert.True(new FileInfo(iconPath).Length > 0);
        Assert.True(File.Exists(FindRepositoryFile("src", "MinecraftInstanceMigration.App", "Assets", "AppIcon.png")));

        string window = File.ReadAllText(FindRepositoryFile("src", "MinecraftInstanceMigration.App", "MainWindow.xaml"));
        Assert.Contains("Icon=\"Assets/AppIcon.ico\"", window, StringComparison.Ordinal);

        string build = File.ReadAllText(FindRepositoryFile("eng", "release", "Build-Release.ps1"));
        string installer = File.ReadAllText(FindRepositoryFile("eng", "release", "installer.iss"));
        Assert.Contains("/DAppIconPath=$iconPath", build, StringComparison.Ordinal);
        Assert.Contains("SetupIconFile={#AppIconPath}", installer, StringComparison.Ordinal);
        Assert.Contains("UninstallDisplayIcon={app}\\{#ExecutableName}", installer, StringComparison.Ordinal);
        Assert.Equal(2, installer.Split("IconFilename: \"{app}\\{#ExecutableName}\"", StringSplitOptions.None).Length - 1);

        using FileStream stream = File.OpenRead(iconPath);
        using BinaryReader reader = new(stream);
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        int count = reader.ReadUInt16();
        Assert.Equal(7, count);

        List<int> sizes = [];
        for (int index = 0; index < count; index++)
        {
            int width = reader.ReadByte();
            int height = reader.ReadByte();
            Assert.Equal(0, reader.ReadByte());
            Assert.Equal(0, reader.ReadByte());
            Assert.Equal(1, reader.ReadUInt16());
            Assert.Equal(32, reader.ReadUInt16());
            uint length = reader.ReadUInt32();
            uint offset = reader.ReadUInt32();

            int size = width == 0 ? 256 : width;
            sizes.Add(size);
            Assert.Equal(size, height == 0 ? 256 : height);
            Assert.True(length > 24);
            Assert.True((long)offset + length <= stream.Length);

            long nextEntry = stream.Position;
            stream.Position = offset;
            Assert.Equal([137, 80, 78, 71, 13, 10, 26, 10], reader.ReadBytes(8));
            stream.Position = offset + 16;
            Assert.Equal(size, BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4)));
            Assert.Equal(size, BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4)));
            stream.Position = nextEntry;
        }

        Assert.Equal([16, 24, 32, 48, 64, 128, 256], sizes);
    }

    [Fact]
    public void PullRequestPackagingDoesNotReceiveSigningSecrets()
    {
        string workflow = File.ReadAllText(FindRepositoryFile(".github", "workflows", "release.yml"));
        int unsignedStart = workflow.IndexOf("Build and verify unsigned release artifacts", StringComparison.Ordinal);
        int signedStart = workflow.IndexOf("Build and verify signed release artifacts", StringComparison.Ordinal);
        int installerValidationStart = workflow.IndexOf("Validate per-user install and uninstall", StringComparison.Ordinal);

        Assert.True(unsignedStart >= 0);
        Assert.True(signedStart > unsignedStart);
        Assert.True(installerValidationStart > signedStart);
        Assert.DoesNotContain(
            "WINDOWS_SIGNING_CERTIFICATE_PASSWORD",
            workflow[unsignedStart..signedStart],
            StringComparison.Ordinal);
        Assert.Contains(
            "if: startsWith(github.ref, 'refs/tags/v')",
            workflow[signedStart..installerValidationStart],
            StringComparison.Ordinal);
        Assert.Contains(
            "secrets.WINDOWS_SIGNING_CERTIFICATE_PASSWORD",
            workflow[signedStart..installerValidationStart],
            StringComparison.Ordinal);
    }

    private static string SingleValue(XDocument document, string name) =>
        Assert.Single(document.Descendants(name)).Value;

    private static string FindRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                string candidate = Path.Combine([directory.FullName, .. segments]);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                break;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository source file was not found.");
    }
}
