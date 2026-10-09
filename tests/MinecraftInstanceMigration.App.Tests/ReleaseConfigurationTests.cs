using System.Buffers.Binary;
using System.Xml.Linq;

namespace MinecraftInstanceMigration.App.Tests;

public sealed class ReleaseConfigurationTests
{
    [Fact]
    public void CentralMetadataDefinesTheReleaseCandidateVersionOnce()
    {
        XDocument props = XDocument.Load(FindRepositoryFile("Directory.Build.props"));

        Assert.Equal("1.0.0", SingleValue(props, "Version"));
        Assert.Equal("1.0.0.0", SingleValue(props, "AssemblyVersion"));
        Assert.Equal("1.0.0.0", SingleValue(props, "FileVersion"));
        Assert.Equal("1.0.0", SingleValue(props, "InformationalVersion"));
        Assert.Equal("false", SingleValue(props, "IncludeSourceRevisionInInformationalVersion"));
        Assert.Equal("Minecraft Instance Migration Tool", SingleValue(props, "Product"));
        Assert.Equal("bosatsuKing", SingleValue(props, "Authors"));
        Assert.Equal("$(MSBuildThisFileDirectory)=/_/", SingleValue(props, "PathMap"));
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
        Assert.Contains("UninstallDisplayName={#ProductName}", installer, StringComparison.Ordinal);
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
            "SM_API_KEY",
            workflow[unsignedStart..signedStart],
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SM_CLIENT_CERT_FILE_B64",
            workflow[unsignedStart..signedStart],
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "MIM_PRODUCTION_SIGNING_CERTIFICATE_",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "if: needs.release-mode.outputs.require-signing == 'true'",
            workflow[signedStart..installerValidationStart],
            StringComparison.Ordinal);
        Assert.Contains(
            "-CertificateThumbprint $env:SIGNING_CERT_THUMBPRINT",
            workflow[signedStart..installerValidationStart],
            StringComparison.Ordinal);
    }

    [Fact]
    public void CommunityReleaseDoesNotRequireSigningAndSignedModeFailsClosed()
    {
        string workflow = File.ReadAllText(FindRepositoryFile(".github", "workflows", "release.yml"));
        int releaseJobStart = workflow.IndexOf("  release-package:", StringComparison.Ordinal);
        int prepareStart = workflow.IndexOf("name: Setup Cloud HSM signing client (DigiCert KeyLocker)", StringComparison.Ordinal);
        int unsignedStart = workflow.IndexOf("name: Build and verify unsigned release artifacts", StringComparison.Ordinal);
        int signedStart = workflow.IndexOf("name: Build and verify signed release artifacts", StringComparison.Ordinal);
        int cleanupStart = workflow.IndexOf("name: Remove temporary cloud signing authentication material", StringComparison.Ordinal);
        int validationStart = workflow.IndexOf("name: Validate per-user install and uninstall", StringComparison.Ordinal);
        int uploadStart = workflow.IndexOf("name: Upload accurately labeled release artifacts", StringComparison.Ordinal);
        int draftStart = workflow.IndexOf("draft-release:", StringComparison.Ordinal);

        Assert.True(prepareStart >= 0);
        Assert.True(releaseJobStart >= 0);
        Assert.True(unsignedStart > prepareStart);
        Assert.True(signedStart > unsignedStart);
        Assert.True(cleanupStart > signedStart);
        Assert.True(validationStart > cleanupStart);
        Assert.True(uploadStart > validationStart);
        Assert.True(draftStart > uploadStart);

        string prepare = workflow[prepareStart..unsignedStart];
        string unsigned = workflow[unsignedStart..signedStart];
        string releaseJob = workflow[releaseJobStart..prepareStart];
        string signed = workflow[signedStart..cleanupStart];
        string cleanup = workflow[cleanupStart..validationStart];
        string uploads = workflow[uploadStart..draftStart];
        string draft = workflow[draftStart..];

        Assert.Contains(
            "name: ${{ needs.release-mode.outputs.environment-name }}",
            releaseJob,
            StringComparison.Ordinal);
        Assert.Contains("if: needs.release-mode.outputs.require-signing == 'true'", prepare, StringComparison.Ordinal);
        Assert.Contains("secrets.SM_API_KEY", prepare, StringComparison.Ordinal);
        Assert.Contains("secrets.SM_CLIENT_CERT_FILE_B64", prepare, StringComparison.Ordinal);
        Assert.Contains("secrets.SM_CLIENT_CERT_PASSWORD", prepare, StringComparison.Ordinal);
        Assert.Contains("MIM_PRODUCTION_SIGNING_CERT_THUMBPRINT", prepare, StringComparison.Ordinal);
        Assert.Contains("vars.MIM_PRODUCTION_SIGNING_TIMESTAMP_URL", prepare, StringComparison.Ordinal);
        Assert.DoesNotContain("MIM_PRODUCTION_SIGNING_CERTIFICATE_BASE64", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("MIM_PRODUCTION_SIGNING_CERTIFICATE_PASSWORD", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets.WINDOWS_SIGNING_CERTIFICATE_", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("vars.WINDOWS_SIGNING_TIMESTAMP_URL", workflow, StringComparison.Ordinal);
        Assert.Contains("smctl windows-cert sync", prepare, StringComparison.Ordinal);
        Assert.Contains("if: needs.release-mode.outputs.require-signing == 'true'", signed, StringComparison.Ordinal);
        Assert.Contains("-RequireSigning", signed, StringComparison.Ordinal);
        Assert.Contains("-CertificateThumbprint $env:SIGNING_CERT_THUMBPRINT", signed, StringComparison.Ordinal);
        Assert.Contains("if: always() && needs.release-mode.outputs.require-signing == 'true'", cleanup, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $clientCert -Force -ErrorAction Stop", cleanup, StringComparison.Ordinal);
        Assert.Contains("Temporary client certificate cleanup failed.", cleanup, StringComparison.Ordinal);
        Assert.Contains("MinecraftInstanceMigrationTool-${{ needs.release-mode.outputs.artifact-kind }}-${{ github.run_id }}", uploads, StringComparison.Ordinal);
        Assert.Contains("MinecraftInstanceMigrationTool-${{ needs.release-mode.outputs.artifact-kind }}-${{ github.run_id }}", draft, StringComparison.Ordinal);
        Assert.Contains("MIM_RELEASE_SIGNING_MODE", workflow, StringComparison.Ordinal);
        Assert.Contains("Signed release was explicitly selected, but signing configuration is incomplete.", prepare, StringComparison.Ordinal);
        Assert.DoesNotContain("exit 0", prepare, StringComparison.Ordinal);
        Assert.DoesNotContain("SIGNING_ENABLED", workflow, StringComparison.Ordinal);
        Assert.Contains("'SM_API_KEY', 'SM_CLIENT_CERT_PASSWORD', 'SM_CLIENT_CERT_FILE', 'SM_HOST'", cleanup, StringComparison.Ordinal);
        Assert.Contains("-Version $version", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void SigningAndChecksumOrderUsesSha256AndFailsClosed()
    {
        string build = File.ReadAllText(FindRepositoryFile("eng", "release", "Build-Release.ps1"));
        string verify = File.ReadAllText(FindRepositoryFile("eng", "release", "Verify-Release.ps1"));
        int appSigning = build.IndexOf("Invoke-AuthenticodeSigning $executable", StringComparison.Ordinal);
        int portablePackage = build.IndexOf("Compress-Archive", StringComparison.Ordinal);
        int installerBuild = build.IndexOf("Invoke-Checked $IsccPath", StringComparison.Ordinal);
        int installerSigning = build.IndexOf("Invoke-AuthenticodeSigning $installerPath", StringComparison.Ordinal);
        int certificateCleanup = build.IndexOf("Remove-TemporarySigningCertificate", build.IndexOf("Assert-SignatureState $installerPath", StringComparison.Ordinal), StringComparison.Ordinal);
        int applicationLaunch = build.IndexOf("Test-PublishedApplication $executable", StringComparison.Ordinal);
        int checksumWrite = build.IndexOf("[System.IO.File]::WriteAllLines($checksumPath", StringComparison.Ordinal);

        Assert.True(appSigning >= 0);
        Assert.True(portablePackage > appSigning);
        Assert.True(installerBuild > portablePackage);
        Assert.True(installerSigning > installerBuild);
        Assert.True(certificateCleanup > installerSigning);
        Assert.True(applicationLaunch > certificateCleanup);
        Assert.True(checksumWrite > applicationLaunch);
        Assert.Contains("\"/fd\", \"SHA256\", \"/td\", \"SHA256\", \"/tr\", $TimestampUrl", build, StringComparison.Ordinal);
        Assert.Contains("\"/sha1\", $CertificateThumbprint", build, StringComparison.Ordinal);
        Assert.Contains("if ([string]::IsNullOrWhiteSpace($SignToolPath)", build, StringComparison.Ordinal);
        Assert.Contains("[string]::IsNullOrWhiteSpace($TimestampUrl)", build, StringComparison.Ordinal);
        Assert.Contains("if ($LASTEXITCODE -ne 0)", build, StringComparison.Ordinal);
        Assert.Contains("$env:MIM_SIGNING_CERTIFICATE_PASSWORD = $null", build, StringComparison.Ordinal);
        Assert.Contains("$env:SIGNING_CERTIFICATE_PATH = $null", build, StringComparison.Ordinal);
        Assert.Contains("Get-AuthenticodeSignature -LiteralPath $Path", build, StringComparison.Ordinal);
        Assert.Contains("Get-AuthenticodeSignature -LiteralPath $path", verify, StringComparison.Ordinal);
        Assert.Contains("if ($RequireSigning -and $signature.Status -ne", verify, StringComparison.Ordinal);
        Assert.Contains("elseif ($signature.Status -ne [System.Management.Automation.SignatureStatus]::NotSigned)", build, StringComparison.Ordinal);
        Assert.Contains("if (-not $RequireSigning -and $signature.Status -ne [System.Management.Automation.SignatureStatus]::NotSigned)", verify, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseDocumentationDistinguishesCommunityReadinessFromOptionalSigning()
    {
        string release = File.ReadAllText(FindRepositoryFile("docs", "release.md"));
        string validation = File.ReadAllText(FindRepositoryFile("docs", "release-validation.md"));

        Assert.Contains("SM_API_KEY", release, StringComparison.Ordinal);
        Assert.Contains("SM_CLIENT_CERT_FILE_B64", release, StringComparison.Ordinal);
        Assert.Contains("MIM_PRODUCTION_SIGNING_CERT_THUMBPRINT", release, StringComparison.Ordinal);
        Assert.Contains("MIM_PRODUCTION_SIGNING_TIMESTAMP_URL", release, StringComparison.Ordinal);
        Assert.Contains("Cloud HSM", release, StringComparison.Ordinal);
        Assert.Contains("KeyLocker", release, StringComparison.Ordinal);
        Assert.Contains("unsigned-dry-run", release, StringComparison.Ordinal);
        Assert.Contains("production-signed", release, StringComparison.Ordinal);
        Assert.Contains("| Production Authenticode signing | NOT APPLICABLE |", validation, StringComparison.Ordinal);
        Assert.Contains("Self-signing is not accepted as evidence.", release, StringComparison.Ordinal);
        Assert.Contains("unsigned-community", release, StringComparison.Ordinal);
        Assert.Contains("not a blocker", release, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseValidationExercisesPackagedApplicationAndReinstall()
    {
        string workflow = File.ReadAllText(FindRepositoryFile(".github", "workflows", "release.yml"));
        string installerTest = File.ReadAllText(FindRepositoryFile("eng", "release", "Test-Installer.ps1"));
        string packageVerification = File.ReadAllText(FindRepositoryFile("eng", "release", "Verify-Release.ps1"));

        Assert.Contains("MIM_UI_EXECUTABLE", workflow, StringComparison.Ordinal);
        Assert.Contains("MinecraftInstanceMigrationTool-$version-win-x64.zip", workflow, StringComparison.Ordinal);
        Assert.Contains("-TestReinstall", workflow, StringComparison.Ordinal);
        Assert.Contains("-CreateDesktopShortcut", workflow, StringComparison.Ordinal);
        Assert.Contains("-UseDefaultInstallPath", workflow, StringComparison.Ordinal);
        Assert.Contains("Start Menu shortcut", installerTest, StringComparison.Ordinal);
        Assert.Contains("Desktop shortcut", installerTest, StringComparison.Ordinal);
        Assert.Contains("must-survive-uninstall.txt", installerTest, StringComparison.Ordinal);
        Assert.Contains("coreclr.dll", packageVerification, StringComparison.Ordinal);
        Assert.Contains("THIRD-PARTY-NOTICES.txt", packageVerification, StringComparison.Ordinal);
        Assert.Contains("ExtractToDirectory", packageVerification, StringComparison.Ordinal);
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
