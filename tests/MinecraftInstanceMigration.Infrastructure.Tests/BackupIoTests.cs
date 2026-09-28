using System.Runtime.Versioning;
using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Infrastructure.Backup;

namespace MinecraftInstanceMigration.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public sealed class BackupIoTests
{
    [Fact]
    public async Task CopiesOwnedDestinationTreeAndWritesManifestOnlyAfterVerification()
    {
        using var fixture = new InspectionFixture();
        string destination = fixture.At("destination");
        string backupParent = fixture.At("backups");
        Directory.CreateDirectory(Path.Combine(destination, "config", "nested"));
        Directory.CreateDirectory(backupParent);
        File.WriteAllText(Path.Combine(destination, "config", "nested", "settings.json"), "settings");
        File.WriteAllText(Path.Combine(destination, "options.txt"), "options");
        string[] before = Snapshot(destination);

        BackupPlan plan = ReadyPlan(
            new BackupPlanEntry("config", ExpectedEntryKind.Directory, EntryState.Directory),
            new BackupPlanEntry("options.txt", ExpectedEntryKind.File, EntryState.File));

        BackupExecutionResult result = await Execute(destination, backupParent, plan);

        Assert.Equal(BackupExecutionStatus.Completed, result.Status);
        Assert.True(result.IsComplete);
        Assert.NotNull(result.BackupRootPath);
        Assert.Equal(2, result.EntriesCopied);
        Assert.Equal("settings", File.ReadAllText(Path.Combine(result.BackupRootPath!, "config", "nested", "settings.json")));
        Assert.Equal("options", File.ReadAllText(Path.Combine(result.BackupRootPath!, "options.txt")));
        Assert.True(File.Exists(Path.Combine(result.BackupRootPath!, ".mim-backup-owner.json")));
        string manifestPath = Path.Combine(result.BackupRootPath!, "backup-manifest.json");
        Assert.True(File.Exists(manifestPath));
        string manifest = File.ReadAllText(manifestPath);
        Assert.DoesNotContain(destination, manifest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(backupParent, manifest, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Verification);
        Assert.Equal(2, result.Verification.FileCount);
        Assert.Equal(2, result.Verification.DirectoryCount);
        Assert.Equal("settings".Length + "options".Length, result.Verification.TotalBytes);
        Assert.Equal(64, result.Verification.Sha256.Length);
        Assert.Equal(before, Snapshot(destination));
    }

    [Fact]
    public async Task BackupExcludesHanemodClientFilesAtEveryDepth()
    {
        using var fixture = new InspectionFixture();
        string destination = fixture.At("destination");
        string backupParent = fixture.At("backups");
        Directory.CreateDirectory(Path.Combine(destination, "config", "nested"));
        Directory.CreateDirectory(backupParent);
        File.WriteAllText(Path.Combine(destination, "config", "normal.json"), "normal");
        File.WriteAllText(Path.Combine(destination, "config", "hanemod-client.json"), "excluded");
        File.WriteAllText(Path.Combine(destination, "config", "nested", "HANEMOD-CLIENT.JSON"), "excluded-nested");

        BackupExecutionResult result = await Execute(
            destination,
            backupParent,
            ReadyPlan(new BackupPlanEntry(
                "config", ExpectedEntryKind.Directory, EntryState.Directory)));

        Assert.Equal(BackupExecutionStatus.Completed, result.Status);
        Assert.Equal("normal", File.ReadAllText(Path.Combine(
            result.BackupRootPath!, "config", "normal.json")));
        Assert.False(File.Exists(Path.Combine(
            result.BackupRootPath!, "config", "hanemod-client.json")));
        Assert.False(File.Exists(Path.Combine(
            result.BackupRootPath!, "config", "nested", "HANEMOD-CLIENT.JSON")));
        Assert.Equal(1, result.Verification!.FileCount);
    }

    [Fact]
    public async Task NestedJunctionFailsClosedAndNeverCopiesTargetPayload()
    {
        using var fixture = new InspectionFixture();
        string destination = fixture.At("destination");
        string backupParent = fixture.At("backups");
        Directory.CreateDirectory(Path.Combine(destination, "config"));
        Directory.CreateDirectory(fixture.At("outside"));
        Directory.CreateDirectory(backupParent);
        File.WriteAllText(fixture.At("outside/secret.txt"), "must-not-copy");
        fixture.Junction("destination/config/linked", "outside");

        BackupExecutionResult result = await Execute(
            destination,
            backupParent,
            ReadyPlan(new BackupPlanEntry("config", ExpectedEntryKind.Directory, EntryState.Directory)));

        Assert.Equal(BackupExecutionStatus.Failed, result.Status);
        Assert.Equal(BackupFailureKind.ReparsePoint, result.FailureKind);
        Assert.NotNull(result.BackupRootPath);
        Assert.False(File.Exists(Path.Combine(result.BackupRootPath!, "backup-manifest.json")));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(result.BackupRootPath!, "*", SearchOption.AllDirectories),
            path => File.ReadAllText(path).Contains("must-not-copy", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BackupParentInsideDestinationIsRejectedBeforeRootCreation()
    {
        using var fixture = new InspectionFixture();
        string destination = fixture.At("destination");
        string backupParent = Path.Combine(destination, "backups");
        Directory.CreateDirectory(Path.Combine(destination, "config"));
        Directory.CreateDirectory(backupParent);

        BackupExecutionResult result = await Execute(
            destination,
            backupParent,
            ReadyPlan(new BackupPlanEntry("config", ExpectedEntryKind.Directory, EntryState.Directory)));

        Assert.Equal(BackupExecutionStatus.Failed, result.Status);
        Assert.Equal(BackupFailureKind.OverlappingRoots, result.FailureKind);
        Assert.Null(result.BackupRootPath);
        Assert.Empty(Directory.EnumerateDirectories(backupParent, "mim-backup-*"));
    }

    [Fact]
    public async Task DestinationKindChangedAfterPlanningLeavesOwnedPartialWithoutCompletionManifest()
    {
        using var fixture = new InspectionFixture();
        string destination = fixture.At("destination");
        string backupParent = fixture.At("backups");
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(backupParent);
        File.WriteAllText(Path.Combine(destination, "config"), "changed-to-file");

        BackupExecutionResult result = await Execute(
            destination,
            backupParent,
            ReadyPlan(new BackupPlanEntry("config", ExpectedEntryKind.Directory, EntryState.Directory)));

        Assert.Equal(BackupExecutionStatus.Failed, result.Status);
        Assert.Equal(BackupFailureKind.SourceChanged, result.FailureKind);
        Assert.NotNull(result.BackupRootPath);
        Assert.True(File.Exists(Path.Combine(result.BackupRootPath!, ".mim-backup-owner.json")));
        Assert.False(File.Exists(Path.Combine(result.BackupRootPath!, "backup-manifest.json")));
    }

    [Fact]
    public async Task PreCancelledStorageRequestCreatesNoBackupRoot()
    {
        using var fixture = new InspectionFixture();
        string destination = fixture.At("destination");
        string backupParent = fixture.At("backups");
        Directory.CreateDirectory(Path.Combine(destination, "config"));
        Directory.CreateDirectory(backupParent);
        BackupPlan plan = ReadyPlan(
            new BackupPlanEntry("config", ExpectedEntryKind.Directory, EntryState.Directory));
        BackupManifestDraft manifest = new BackupPlanner().CreateManifestDraft(plan);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        BackupExecutionResult result = await new WindowsBackupStorage().CreateBackupAsync(
            destination,
            backupParent,
            plan,
            manifest,
            cancellation.Token);

        Assert.Equal(BackupExecutionStatus.Cancelled, result.Status);
        Assert.Null(result.BackupRootPath);
        Assert.Empty(Directory.EnumerateDirectories(backupParent, "mim-backup-*"));
    }

    private static Task<BackupExecutionResult> Execute(
        string destination,
        string backupParent,
        BackupPlan plan) =>
        new BackupExecutor(new WindowsBackupStorage(), new BackupPlanner()).ExecuteAsync(
            destination,
            backupParent,
            plan,
            TestContext.Current.CancellationToken);

    private static BackupPlan ReadyPlan(params BackupPlanEntry[] entries) =>
        new(BackupPlanStatus.Ready, entries, []);

    private static string[] Snapshot(string root) =>
        Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Prepend(root)
            .Order(StringComparer.Ordinal)
            .Select(path =>
            {
                FileAttributes attributes = File.GetAttributes(path);
                string content = attributes.HasFlag(FileAttributes.Directory)
                    ? ""
                    : Convert.ToHexString(File.ReadAllBytes(path));
                return $"{Path.GetRelativePath(root, path)}|{attributes}|{content}";
            })
            .ToArray();
}
