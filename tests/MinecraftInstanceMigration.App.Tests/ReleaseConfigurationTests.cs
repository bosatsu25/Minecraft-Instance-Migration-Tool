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
