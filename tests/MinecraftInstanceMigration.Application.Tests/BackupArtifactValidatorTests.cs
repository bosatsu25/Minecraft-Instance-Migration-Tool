using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Application.Tests;

public sealed class BackupArtifactValidatorTests
{
    [Fact]
    public async Task InvalidPlanNeverCrossesStorageBoundary()
    {
        var storage = new StubStorage();
        var validator = new BackupArtifactValidator(storage);
        var plan = new BackupPlan(
            BackupPlanStatus.Blocked,
            [],
            [new BackupBlocker(BackupBlockerKind.MigrationPlanNotReady)]);

        BackupArtifactValidationResult result = await validator.ValidateAsync(
            "backup",
            plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(BackupArtifactValidationStatus.Invalid, result.Status);
        Assert.Equal(BackupArtifactFailureKind.InvalidPlan, result.FailureKind);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task PreCancelledRequestNeverCrossesStorageBoundary()
    {
        var storage = new StubStorage();
        var validator = new BackupArtifactValidator(storage);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        BackupArtifactValidationResult result = await validator.ValidateAsync(
            "backup",
            ReadyPlan(),
            cancellation.Token);

        Assert.Equal(BackupArtifactValidationStatus.Cancelled, result.Status);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task ReadyPlanDelegatesExactlyOnce()
    {
        var storage = new StubStorage
        {
            Result = new BackupArtifactValidationResult(
                BackupArtifactValidationStatus.Valid,
                Verification: new BackupVerificationSummary(1, 1, 10, new string('A', 64))),
        };
        var validator = new BackupArtifactValidator(storage);

        BackupArtifactValidationResult result = await validator.ValidateAsync(
            "backup",
            ReadyPlan(),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Equal(1, storage.Calls);
    }

    private static BackupPlan ReadyPlan() =>
        new(
            BackupPlanStatus.Ready,
            [new BackupPlanEntry("config", ExpectedEntryKind.Directory, EntryState.Directory)],
            []);

    private sealed class StubStorage : IBackupArtifactValidationStorage
    {
        public int Calls { get; private set; }

        public BackupArtifactValidationResult Result { get; init; } =
            new(BackupArtifactValidationStatus.Valid);

        public Task<BackupArtifactValidationResult> ValidateBackupAsync(
            string backupRoot,
            BackupPlan plan,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }
}
