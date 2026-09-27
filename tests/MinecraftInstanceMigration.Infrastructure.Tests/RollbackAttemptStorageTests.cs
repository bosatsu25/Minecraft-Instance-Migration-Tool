using System.Runtime.Versioning;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Infrastructure.Execution;

namespace MinecraftInstanceMigration.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public sealed class RollbackAttemptStorageTests
{
    [Fact]
    public async Task CreatePersistsPathFreeHeaderAndLoadsNotStarted()
    {
        using var fixture = new InspectionFixture();
        string destination = fixture.At("destination");
        string journals = fixture.At("journals");
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(journals);
        RollbackPlan plan = TwoActionPlan();

        RollbackAttemptWriteResult created =
            await Store().CreateAsync(
                journals,
                destination,
                null,
                plan,
                TestContext.Current.CancellationToken);

        Assert.True(created.IsSuccess);
        Assert.NotNull(created.Attempt);
        Assert.True(File.Exists(created.Attempt.JournalPath));

        string persisted = File.ReadAllText(created.Attempt.JournalPath);
        Assert.DoesNotContain(fixture.Root, persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(destination, persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(journals, persisted, StringComparison.OrdinalIgnoreCase);

        RollbackAttemptReadResult loaded =
            await Store().LoadAsync(
                created.Attempt,
                plan,
                TestContext.Current.CancellationToken);

        Assert.True(loaded.IsLoaded);
        Assert.All(
            loaded.Snapshot!.Steps,
            step => Assert.Equal(
                RollbackAttemptStepOutcome.NotStarted,
                step.Outcome));
    }

    [Fact]
    public async Task DurableStartedWithoutTerminalLoadsAsUncertain()
    {
        using var fixture = new InspectionFixture();
        RollbackPlan plan = OneActionPlan();
        RollbackAttemptReference attempt =
            await Create(fixture, plan);
        WindowsRollbackAttemptStorage storage = Store();

        Assert.True((await storage.MarkActionStartedAsync(
            attempt,
            plan,
            0,
            TestContext.Current.CancellationToken)).IsSuccess);

        RollbackAttemptReadResult loaded =
            await storage.LoadAsync(
                attempt,
                plan,
                TestContext.Current.CancellationToken);

        RollbackAttemptStep step =
            Assert.Single(loaded.Snapshot!.Steps);
        Assert.Equal(
            RollbackAttemptStepOutcome.Uncertain,
            step.Outcome);
        Assert.True(loaded.Snapshot.HasUncertainAction);
        Assert.True(loaded.Snapshot.RequiresRecovery);
    }

    [Fact]
    public async Task AppliedTerminalSurvivesReload()
    {
        using var fixture = new InspectionFixture();
        RollbackPlan plan = OneActionPlan();
        RollbackAttemptReference attempt =
            await Create(fixture, plan);
        WindowsRollbackAttemptStorage storage = Store();

        Assert.True((await storage.MarkActionStartedAsync(
            attempt,
            plan,
            0,
            TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await storage.MarkActionAppliedAsync(
            attempt,
            plan,
            0,
            TestContext.Current.CancellationToken)).IsSuccess);

        RollbackAttemptReadResult loaded =
            await storage.LoadAsync(
                attempt,
                plan,
                TestContext.Current.CancellationToken);

        RollbackAttemptStep step =
            Assert.Single(loaded.Snapshot!.Steps);
        Assert.Equal(
            RollbackAttemptStepOutcome.Applied,
            step.Outcome);
        Assert.False(loaded.Snapshot.RequiresRecovery);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailureTerminalSurvivesReload(bool guardRejected)
    {
        using var fixture = new InspectionFixture();
        RollbackPlan plan = OneActionPlan();
        RollbackAttemptReference attempt =
            await Create(fixture, plan);
        WindowsRollbackAttemptStorage storage = Store();

        Assert.True((await storage.MarkActionStartedAsync(
            attempt,
            plan,
            0,
            TestContext.Current.CancellationToken)).IsSuccess);

        RollbackAttemptWriteResult terminal =
            guardRejected
                ? await storage.MarkActionGuardRejectedAsync(
                    attempt,
                    plan,
                    0,
                    RollbackStorageFailureKind.DestinationChanged,
                    TestContext.Current.CancellationToken)
                : await storage.MarkActionFailedAsync(
                    attempt,
                    plan,
                    0,
                    RollbackStorageFailureKind.VerificationFailed,
                    TestContext.Current.CancellationToken);

        Assert.True(terminal.IsSuccess);

        RollbackAttemptReadResult loaded =
            await storage.LoadAsync(
                attempt,
                plan,
                TestContext.Current.CancellationToken);

        RollbackAttemptStep step =
            Assert.Single(loaded.Snapshot!.Steps);
        Assert.Equal(
            guardRejected
                ? RollbackAttemptStepOutcome.GuardRejected
                : RollbackAttemptStepOutcome.Failed,
            step.Outcome);
        Assert.Equal(
            guardRejected
                ? RollbackStorageFailureKind.DestinationChanged
                : RollbackStorageFailureKind.VerificationFailed,
            step.FailureKind);
        Assert.Equal(
            !guardRejected,
            loaded.Snapshot.RequiresRecovery);
    }

    [Fact]
    public async Task LaterActionCannotStartBeforePriorApplied()
    {
        using var fixture = new InspectionFixture();
        RollbackPlan plan = TwoActionPlan();
        RollbackAttemptReference attempt =
            await Create(fixture, plan);
        WindowsRollbackAttemptStorage storage = Store();

        RollbackAttemptWriteResult early =
            await storage.MarkActionStartedAsync(
                attempt,
                plan,
                1,
                TestContext.Current.CancellationToken);

        Assert.Equal(RollbackAttemptWriteStatus.Invalid, early.Status);
        Assert.Equal(
            RollbackAttemptPersistenceFailureKind.JournalStateInvalid,
            early.FailureKind);

        Assert.True((await storage.MarkActionStartedAsync(
            attempt,
            plan,
            0,
            TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await storage.MarkActionAppliedAsync(
            attempt,
            plan,
            0,
            TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await storage.MarkActionStartedAsync(
            attempt,
            plan,
            1,
            TestContext.Current.CancellationToken)).IsSuccess);
    }

    [Fact]
    public async Task TornTerminalIsIgnoredThenTruncatedBeforeNextAppend()
    {
        using var fixture = new InspectionFixture();
        RollbackPlan plan = OneActionPlan();
        RollbackAttemptReference attempt =
            await Create(fixture, plan);
        WindowsRollbackAttemptStorage storage = Store();

        Assert.True((await storage.MarkActionStartedAsync(
            attempt,
            plan,
            0,
            TestContext.Current.CancellationToken)).IsSuccess);

        await File.AppendAllTextAsync(
            attempt.JournalPath,
            "{\"payload\":",
            TestContext.Current.CancellationToken);

        RollbackAttemptReadResult afterCrash =
            await storage.LoadAsync(
                attempt,
                plan,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            RollbackAttemptStepOutcome.Uncertain,
            Assert.Single(afterCrash.Snapshot!.Steps).Outcome);

        Assert.True((await storage.MarkActionFailedAsync(
            attempt,
            plan,
            0,
            RollbackStorageFailureKind.IoFailure,
            TestContext.Current.CancellationToken)).IsSuccess);

        Assert.EndsWith(
            "\n",
            File.ReadAllText(attempt.JournalPath),
            StringComparison.Ordinal);

        RollbackAttemptReadResult recovered =
            await storage.LoadAsync(
                attempt,
                plan,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            RollbackAttemptStepOutcome.Failed,
            Assert.Single(recovered.Snapshot!.Steps).Outcome);
    }

    [Fact]
    public async Task JournalInsideDestinationIsRejectedWithoutArtifact()
    {
        using var fixture = new InspectionFixture();
        string destination = fixture.At("destination");
        string journals = fixture.At("destination/journals");
        Directory.CreateDirectory(journals);

        RollbackAttemptWriteResult result =
            await Store().CreateAsync(
                journals,
                destination,
                null,
                OneActionPlan(),
                TestContext.Current.CancellationToken);

        Assert.Equal(RollbackAttemptWriteStatus.Invalid, result.Status);
        Assert.Equal(
            RollbackAttemptPersistenceFailureKind.UnsafeWorkspace,
            result.FailureKind);
        Assert.Empty(Directory.EnumerateFiles(
            journals,
            "mim-rollback-*.jsonl"));
    }

    [Fact]
    public async Task DifferentPlanCannotReuseAttemptJournal()
    {
        using var fixture = new InspectionFixture();
        RollbackPlan plan = OneActionPlan();
        RollbackAttemptReference attempt =
            await Create(fixture, plan);

        var different = new RollbackPlan(
            RollbackPlanStatus.Ready,
            [
                new RollbackPlanEntry(
                    0,
                    "config",
                    ExpectedEntryKind.Directory,
                    ExecutionOperationKind.Copy,
                    RollbackActionKind.DeleteCreatedEntry,
                    Fingerprint('C')),
            ],
            []);

        RollbackAttemptReadResult loaded =
            await Store().LoadAsync(
                attempt,
                different,
                TestContext.Current.CancellationToken);

        Assert.Equal(RollbackAttemptReadStatus.Invalid, loaded.Status);
        Assert.Equal(
            RollbackAttemptPersistenceFailureKind.JournalFormatInvalid,
            loaded.FailureKind);
    }

    private static WindowsRollbackAttemptStorage Store() => new();

    private static RollbackPlan OneActionPlan() =>
        new(
            RollbackPlanStatus.Ready,
            [
                new RollbackPlanEntry(
                    0,
                    "options.txt",
                    ExpectedEntryKind.File,
                    ExecutionOperationKind.Copy,
                    RollbackActionKind.DeleteCreatedEntry,
                    Fingerprint('A')),
            ],
            []);

    private static RollbackPlan TwoActionPlan() =>
        new(
            RollbackPlanStatus.Ready,
            [
                new RollbackPlanEntry(
                    0,
                    "config",
                    ExpectedEntryKind.Directory,
                    ExecutionOperationKind.Replace,
                    RollbackActionKind.RestoreFromBackup,
                    Fingerprint('A')),
                new RollbackPlanEntry(
                    1,
                    "options.txt",
                    ExpectedEntryKind.File,
                    ExecutionOperationKind.Copy,
                    RollbackActionKind.DeleteCreatedEntry,
                    Fingerprint('B')),
            ],
            []);

    private static ExecutionContentFingerprint Fingerprint(char seed) =>
        new(1, 0, 4, new string(seed, 64));

    private static async Task<RollbackAttemptReference> Create(
        InspectionFixture fixture,
        RollbackPlan plan)
    {
        string destination = fixture.At("destination");
        string journals = fixture.At("journals");
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(journals);

        RollbackAttemptWriteResult result =
            await Store().CreateAsync(
                journals,
                destination,
                null,
                plan,
                TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        return Assert.IsType<RollbackAttemptReference>(
            result.Attempt);
    }
}
