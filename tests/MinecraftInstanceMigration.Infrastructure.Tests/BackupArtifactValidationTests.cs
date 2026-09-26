using System.Runtime.Versioning;
using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Infrastructure.Backup;

namespace MinecraftInstanceMigration.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public sealed class BackupArtifactValidationTests
{
    [Fact]
    public async Task CompletedBackupRevalidatesReadOnly()
    {
        using var fixture = new InspectionFixture();
        string destination = fixture.At("destination");
        string backupParent = fixture.At("backups");
        Directory.CreateDirectory(Path.Combine(destination, "config", "nested"));
        Directory.CreateDirectory(backupParent);
        File.WriteAllText(Path.Combine(destination, "config", "nested", "settings.json"), "settings");

        BackupPlan plan = ReadyPlan(
            new BackupPlanEntry("config", ExpectedEntryKind.Directory, EntryState.Directory));
        BackupExecutionResult execution = await CreateBackup(destination, backupParent, plan);
        Assert.Equal(BackupExecutionStatus.Completed, execution.Status);
        Assert.NotNull(execution.BackupRootPath);

        string[] before = Snapshot(execution.BackupRootPath!);
        BackupArtifactValidationResult validation = await Validate(execution.BackupRootPath!, plan);

        Assert.True(validation.IsValid);
        Assert.NotNull(validation.Verification);
        Assert.Equal(execution.Verification, validation.Verification);
        Assert.Equal(before, Snapshot(execution.BackupRootPath!));
    }

    [Fact]
    public async Task PayloadMutationInvalidatesCompletedBackup()
    {
        using var fixture = new InspectionFixture();
        string destination = fixture.At("destination");
        string backupParent = fixture.At("backups");
        Directory.CreateDirectory(Path.Combine(destination, "config"));
        Directory.CreateDirectory(backupParent);
        File.WriteAllText(Path.Combine(destination, "config", "settings.json"), "original");

        BackupPlan plan = ReadyPlan(
            new BackupPlanEntry("config", ExpectedEntryKind.Directory, EntryState.Directory));
        BackupExecutionResult execution = await CreateBackup(destination, backupParent, plan);
        File.WriteAllText(Path.Combine(execution.BackupRootPath!, "config", "settings.json"), "tampered");

        BackupArtifactValidationResult validation = await Validate(execution.BackupRootPath!, plan);

        Assert.Equal(BackupArtifactValidationStatus.Invalid, validation.Status);
        Assert.Equal(BackupArtifactFailureKind.VerificationMismatch, validation.FailureKind);
    }

    [Fact]
    public async Task MissingCompletionManifestIsNotAValidBackup()
    {
        using var fixture = new InspectionFixture();
        string destination = fixture.At("destination");
        string backupParent = fixture.At("backups");
        Directory.CreateDirectory(Path.Combine(destination, "config"));
        Directory.CreateDirectory(backupParent);

        BackupPlan plan = ReadyPlan(
            new BackupPlanEntry("config", ExpectedEntryKind.Directory, EntryState.Directory));
        BackupExecutionResult execution = await CreateBackup(destination, backupParent, plan);
        File.Delete(Path.Combine(execution.BackupRootPath!, "backup-manifest.json"));

        BackupArtifactValidationResult validation = await Validate(execution.BackupRootPath!, plan);

        Assert.Equal(BackupArtifactValidationStatus.Invalid, validation.Status);
        Assert.Equal(BackupArtifactFailureKind.ManifestInvalid, validation.FailureKind);
    }

    [Fact]
    public async Task InvalidOwnershipMarkerIsRejected()
    {
        using var fixture = new InspectionFixture();
        string destination = fixture.At("destination");
        string backupParent = fixture.At("backups");
        Directory.CreateDirectory(Path.Combine(destination, "config"));
        Directory.CreateDirectory(backupParent);

        BackupPlan plan = ReadyPlan(
            new BackupPlanEntry("config", ExpectedEntryKind.Directory, EntryState.Directory));
        BackupExecutionResult execution = await CreateBackup(destination, backupParent, plan);
        File.WriteAllText(Path.Combine(execution.BackupRootPath!, ".mim-backup-owner.json"), "{}");

        BackupArtifactValidationResult validation = await Validate(execution.BackupRootPath!, plan);

        Assert.Equal(BackupArtifactValidationStatus.Invalid, validation.Status);
        Assert.Equal(BackupArtifactFailureKind.OwnershipMarkerInvalid, validation.FailureKind);
    }

    [Fact]
    public async Task UnexpectedTopLevelContentIsRejected()
    {
        using var fixture = new InspectionFixture();
        string destination = fixture.At("destination");
        string backupParent = fixture.At("backups");
        Directory.CreateDirectory(Path.Combine(destination, "config"));
        Directory.CreateDirectory(backupParent);

        BackupPlan plan = ReadyPlan(
            new BackupPlanEntry("config", ExpectedEntryKind.Directory, EntryState.Directory));
        BackupExecutionResult execution = await CreateBackup(destination, backupParent, plan);
        File.WriteAllText(Path.Combine(execution.BackupRootPath!, "unexpected.txt"), "unexpected");

        BackupArtifactValidationResult validation = await Validate(execution.BackupRootPath!, plan);

        Assert.Equal(BackupArtifactValidationStatus.Invalid, validation.Status);
        Assert.Equal(BackupArtifactFailureKind.UnexpectedContent, validation.FailureKind);
    }

    [Fact]
    public async Task NestedReparsePointInjectedAfterCompletionIsRejected()
    {
        using var fixture = new InspectionFixture();
        string destination = fixture.At("destination");
        string backupParent = fixture.At("backups");
        Directory.CreateDirectory(Path.Combine(destination, "config"));
        Directory.CreateDirectory(backupParent);
        Directory.CreateDirectory(fixture.At("outside"));
        File.WriteAllText(fixture.At("outside/secret.txt"), "must-not-read");

        BackupPlan plan = ReadyPlan(
            new BackupPlanEntry("config", ExpectedEntryKind.Directory, EntryState.Directory));
        BackupExecutionResult execution = await CreateBackup(destination, backupParent, plan);
        string backupRelative = Path.GetRelativePath(fixture.Root, execution.BackupRootPath!);
        fixture.Junction(Path.Combine(backupRelative, "config", "linked"), "outside");

        BackupArtifactValidationResult validation = await Validate(execution.BackupRootPath!, plan);

        Assert.Equal(BackupArtifactValidationStatus.Invalid, validation.Status);
        Assert.Equal(BackupArtifactFailureKind.ReparsePoint, validation.FailureKind);
    }

    [Fact]
    public async Task ManifestFromDifferentPlanIsRejected()
    {
        using var fixture = new InspectionFixture();
        string destination = fixture.At("destination");
        string backupParent = fixture.At("backups");
        Directory.CreateDirectory(Path.Combine(destination, "config"));
        Directory.CreateDirectory(Path.Combine(destination, "options.txt"));
        Directory.CreateDirectory(backupParent);

        BackupPlan configPlan = ReadyPlan(
            new BackupPlanEntry("config", ExpectedEntryKind.Directory, EntryState.Directory));
        BackupExecutionResult execution = await CreateBackup(destination, backupParent, configPlan);

        BackupPlan differentPlan = ReadyPlan(
            new BackupPlanEntry("options.txt", ExpectedEntryKind.File, EntryState.File));

        BackupArtifactValidationResult validation = await Validate(execution.BackupRootPath!, differentPlan);

        Assert.Equal(BackupArtifactValidationStatus.Invalid, validation.Status);
        Assert.Equal(BackupArtifactFailureKind.ManifestInvalid, validation.FailureKind);
    }

    private static Task<BackupExecutionResult> CreateBackup(
        string destination,
        string backupParent,
        BackupPlan plan) =>
        new BackupExecutor(new WindowsBackupStorage(), new BackupPlanner()).ExecuteAsync(
            destination,
            backupParent,
            plan,
            TestContext.Current.CancellationToken);

    private static Task<BackupArtifactValidationResult> Validate(
        string backupRoot,
        BackupPlan plan) =>
        new BackupArtifactValidator(new WindowsBackupStorage()).ValidateAsync(
            backupRoot,
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
