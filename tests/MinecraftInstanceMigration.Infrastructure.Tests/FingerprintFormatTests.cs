using System.Runtime.Versioning;
using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Infrastructure.Backup;
using MinecraftInstanceMigration.Infrastructure.Execution;

namespace MinecraftInstanceMigration.Infrastructure.Tests;

// Fingerprints are persisted in execution journals and backup manifests written by earlier releases,
// and rollback compares against them. The expected digests were computed independently from the
// documented encoding, so any change to traversal order, exclusion, or framing fails here first.
[SupportedOSPlatform("windows")]
public sealed class FingerprintFormatTests
{
    private const string ConfigSha256 = "7485C7FEF56A7FA5998D468DEB24673FB2F24F2B152DEB55FA1DB5B254B32CCE";
    private const string BackupPlanSha256 = "BE419DE5C98545F30C69DB59C29FF4B821826B3A0BEC4F4FAAD59F3BB3F90FAF";

    [Fact]
    public void LiveEntryFingerprintMatchesPersistedEncoding()
    {
        using var fixture = new InspectionFixture();
        string root = CreateInstance(fixture, "instance");

        using WindowsExecutionTree.HeldDirectory held =
            WindowsExecutionTree.OpenDirectoryChain(root, writableFinal: false);
        WindowsExecutionTree.TreeFingerprint fingerprint = WindowsExecutionTree.FingerprintNode(
            held.Root,
            "config",
            ExpectedEntryKind.Directory,
            TestContext.Current.CancellationToken);

        Assert.Equal(new WindowsExecutionTree.TreeFingerprint(2, 2, 9, ConfigSha256), fingerprint);
    }

    [Fact]
    public void HeldTreeFingerprintMatchesLiveTraversal()
    {
        using var fixture = new InspectionFixture();
        string root = CreateInstance(fixture, "instance");

        using WindowsExecutionTree.HeldDirectory held =
            WindowsExecutionTree.OpenDirectoryChain(root, writableFinal: false);
        using WindowsExecutionTree.HeldNodeTree tree =
            WindowsExecutionTree.OpenHeldTree(held.Root, "config", forDelete: false);
        WindowsExecutionTree.TreeFingerprint fingerprint =
            WindowsExecutionTree.FingerprintHeldTree(tree, TestContext.Current.CancellationToken);

        Assert.Equal(new WindowsExecutionTree.TreeFingerprint(2, 2, 9, ConfigSha256), fingerprint);
    }

    [Fact]
    public async Task BackupManifestVerificationMatchesPersistedEncoding()
    {
        using var fixture = new InspectionFixture();
        string destination = CreateInstance(fixture, "destination");
        string backupParent = fixture.At("backups");
        Directory.CreateDirectory(backupParent);
        var plan = new BackupPlan(
            BackupPlanStatus.Ready,
            [
                new BackupPlanEntry("config", ExpectedEntryKind.Directory, EntryState.Directory),
                new BackupPlanEntry("options.txt", ExpectedEntryKind.File, EntryState.File),
            ],
            []);

        BackupExecutionResult result = await new WindowsBackupStorage().CreateBackupAsync(
            destination,
            backupParent,
            plan,
            BackupPlanPolicy.CreateManifestDraft(plan),
            TestContext.Current.CancellationToken);

        Assert.Equal(BackupExecutionStatus.Completed, result.Status);
        Assert.Equal(new BackupVerificationSummary(3, 2, 16, BackupPlanSha256), result.Verification);

        BackupArtifactValidationResult validation = await new WindowsBackupStorage().ValidateBackupAsync(
            result.BackupRootPath!,
            plan,
            TestContext.Current.CancellationToken);

        Assert.True(validation.IsValid);
        Assert.Equal(result.Verification, validation.Verification);
    }

    // config/{a.txt, hanemod-client.json (excluded), nested/b.json} and options.txt.
    private static string CreateInstance(InspectionFixture fixture, string name)
    {
        string root = fixture.At(name);
        Directory.CreateDirectory(Path.Combine(root, "config", "nested"));
        File.WriteAllText(Path.Combine(root, "config", "a.txt"), "alpha");
        File.WriteAllText(Path.Combine(root, "config", "hanemod-client.json"), "excluded");
        File.WriteAllText(Path.Combine(root, "config", "nested", "b.json"), "beta");
        File.WriteAllText(Path.Combine(root, "options.txt"), "options");
        return root;
    }
}
