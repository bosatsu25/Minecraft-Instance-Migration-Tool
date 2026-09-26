using System.Runtime.Versioning;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Infrastructure.Execution;

namespace MinecraftInstanceMigration.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public sealed class ExecutionJournalStorageTests
{
    [Fact]
    public async Task CreatePersistsHeaderWithoutAbsolutePathsAndLoadsNotStarted()
    {
        using var fixture = new InspectionFixture();
        string parent = fixture.At("journals");
        Directory.CreateDirectory(parent);
        ExecutionJournalDraft draft = Draft(
            new ExecutionJournalEntry(0, "options.txt", ExpectedEntryKind.File, ExecutionOperationKind.Copy),
            new ExecutionJournalEntry(1, "config", ExpectedEntryKind.Directory, ExecutionOperationKind.Replace));

        ExecutionJournalWriteResult created = await Store().CreateAsync(
            parent,
            draft,
            TestContext.Current.CancellationToken);

        Assert.True(created.IsSuccess);
        Assert.NotNull(created.Journal);
        Assert.True(File.Exists(created.Journal.JournalPath));
        Assert.Equal(
            $"mim-journal-{created.Journal.JournalId}.jsonl",
            Path.GetFileName(created.Journal.JournalPath));

        string persisted = File.ReadAllText(created.Journal.JournalPath);
        Assert.DoesNotContain(fixture.Root, persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(parent, persisted, StringComparison.OrdinalIgnoreCase);

        ExecutionJournalReadResult loaded = await Store().LoadAsync(
            created.Journal,
            draft,
            TestContext.Current.CancellationToken);

        Assert.True(loaded.IsLoaded);
        Assert.All(
            loaded.Snapshot!.Steps,
            step => Assert.Equal(ExecutionStepOutcome.NotStarted, step.Outcome));
    }

    [Fact]
    public async Task DurableStartedWithoutTerminalLoadsAsUncertain()
    {
        using var fixture = new InspectionFixture();
        ExecutionJournalDraft draft = OneStepDraft();
        ExecutionJournalReference journal = await Create(fixture, draft);
        WindowsExecutionJournalStorage storage = Store();

        ExecutionJournalWriteResult started = await storage.MarkStepStartedAsync(
            journal,
            draft,
            0,
            TestContext.Current.CancellationToken);
        ExecutionJournalReadResult loaded = await storage.LoadAsync(
            journal,
            draft,
            TestContext.Current.CancellationToken);

        Assert.True(started.IsSuccess);
        ExecutionJournalStep step = Assert.Single(loaded.Snapshot!.Steps);
        Assert.Equal(ExecutionStepOutcome.Uncertain, step.Outcome);
        Assert.Null(step.AppliedFingerprint);
    }

    [Fact]
    public async Task AppliedFingerprintSurvivesReload()
    {
        using var fixture = new InspectionFixture();
        ExecutionJournalDraft draft = OneStepDraft();
        ExecutionJournalReference journal = await Create(fixture, draft);
        WindowsExecutionJournalStorage storage = Store();
        ExecutionContentFingerprint fingerprint =
            new(2, 1, 42, new string('B', 64));

        Assert.True((await storage.MarkStepStartedAsync(
            journal,
            draft,
            0,
            TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await storage.MarkStepAppliedAsync(
            journal,
            draft,
            0,
            fingerprint,
            TestContext.Current.CancellationToken)).IsSuccess);

        ExecutionJournalReadResult loaded = await storage.LoadAsync(
            journal,
            draft,
            TestContext.Current.CancellationToken);

        ExecutionJournalStep step = Assert.Single(loaded.Snapshot!.Steps);
        Assert.Equal(ExecutionStepOutcome.Applied, step.Outcome);
        Assert.Equal(fingerprint, step.AppliedFingerprint);
    }

    [Fact]
    public async Task TornTerminalRecordIsIgnoredThenTruncatedBeforeNextDurableTransition()
    {
        using var fixture = new InspectionFixture();
        ExecutionJournalDraft draft = OneStepDraft();
        ExecutionJournalReference journal = await Create(fixture, draft);
        WindowsExecutionJournalStorage storage = Store();

        Assert.True((await storage.MarkStepStartedAsync(
            journal,
            draft,
            0,
            TestContext.Current.CancellationToken)).IsSuccess);

        await File.AppendAllTextAsync(
            journal.JournalPath,
            "{\"payload\":",
            TestContext.Current.CancellationToken);

        ExecutionJournalReadResult afterCrash = await storage.LoadAsync(
            journal,
            draft,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            ExecutionStepOutcome.Uncertain,
            Assert.Single(afterCrash.Snapshot!.Steps).Outcome);

        ExecutionContentFingerprint fingerprint =
            new(1, 0, 7, new string('C', 64));
        ExecutionJournalWriteResult applied = await storage.MarkStepAppliedAsync(
            journal,
            draft,
            0,
            fingerprint,
            TestContext.Current.CancellationToken);
        Assert.True(applied.IsSuccess);
        Assert.EndsWith("\n", File.ReadAllText(journal.JournalPath), StringComparison.Ordinal);

        ExecutionJournalReadResult recovered = await storage.LoadAsync(
            journal,
            draft,
            TestContext.Current.CancellationToken);
        ExecutionJournalStep step = Assert.Single(recovered.Snapshot!.Steps);
        Assert.Equal(ExecutionStepOutcome.Applied, step.Outcome);
        Assert.Equal(fingerprint, step.AppliedFingerprint);
    }

    [Fact]
    public async Task CompletedMalformedRecordFailsClosed()
    {
        using var fixture = new InspectionFixture();
        ExecutionJournalDraft draft = OneStepDraft();
        ExecutionJournalReference journal = await Create(fixture, draft);

        await File.AppendAllTextAsync(
            journal.JournalPath,
            "{\"corrupt\":true}\n",
            TestContext.Current.CancellationToken);

        ExecutionJournalReadResult loaded = await Store().LoadAsync(
            journal,
            draft,
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionJournalReadStatus.Invalid, loaded.Status);
        Assert.Equal(
            ExecutionJournalPersistenceFailureKind.JournalFormatInvalid,
            loaded.FailureKind);
    }

    [Fact]
    public async Task ChecksumMutationFailsClosed()
    {
        using var fixture = new InspectionFixture();
        ExecutionJournalDraft draft = OneStepDraft();
        ExecutionJournalReference journal = await Create(fixture, draft);
        WindowsExecutionJournalStorage storage = Store();

        Assert.True((await storage.MarkStepStartedAsync(
            journal,
            draft,
            0,
            TestContext.Current.CancellationToken)).IsSuccess);

        string text = File.ReadAllText(journal.JournalPath);
        File.WriteAllText(
            journal.JournalPath,
            text.Replace("\"Started\"", "\"Failed\"", StringComparison.Ordinal));

        ExecutionJournalReadResult loaded = await storage.LoadAsync(
            journal,
            draft,
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionJournalReadStatus.Invalid, loaded.Status);
        Assert.Equal(
            ExecutionJournalPersistenceFailureKind.JournalFormatInvalid,
            loaded.FailureKind);
    }

    [Fact]
    public async Task CannotStartLaterStepBeforePriorStepIsDurablyApplied()
    {
        using var fixture = new InspectionFixture();
        ExecutionJournalDraft draft = Draft(
            new ExecutionJournalEntry(0, "options.txt", ExpectedEntryKind.File, ExecutionOperationKind.Copy),
            new ExecutionJournalEntry(1, "config", ExpectedEntryKind.Directory, ExecutionOperationKind.Replace));
        ExecutionJournalReference journal = await Create(fixture, draft);
        WindowsExecutionJournalStorage storage = Store();

        ExecutionJournalWriteResult tooEarly = await storage.MarkStepStartedAsync(
            journal,
            draft,
            1,
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionJournalWriteStatus.Invalid, tooEarly.Status);
        Assert.Equal(
            ExecutionJournalPersistenceFailureKind.JournalStateInvalid,
            tooEarly.FailureKind);

        Assert.True((await storage.MarkStepStartedAsync(
            journal,
            draft,
            0,
            TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await storage.MarkStepAppliedAsync(
            journal,
            draft,
            0,
            new ExecutionContentFingerprint(1, 0, 1, new string('D', 64)),
            TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await storage.MarkStepStartedAsync(
            journal,
            draft,
            1,
            TestContext.Current.CancellationToken)).IsSuccess);
    }

    [Fact]
    public async Task FailedStepPreventsLaterStepFromStarting()
    {
        using var fixture = new InspectionFixture();
        ExecutionJournalDraft draft = Draft(
            new ExecutionJournalEntry(0, "options.txt", ExpectedEntryKind.File, ExecutionOperationKind.Copy),
            new ExecutionJournalEntry(1, "config", ExpectedEntryKind.Directory, ExecutionOperationKind.Replace));
        ExecutionJournalReference journal = await Create(fixture, draft);
        WindowsExecutionJournalStorage storage = Store();

        Assert.True((await storage.MarkStepStartedAsync(
            journal,
            draft,
            0,
            TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await storage.MarkStepFailedAsync(
            journal,
            draft,
            0,
            TestContext.Current.CancellationToken)).IsSuccess);

        ExecutionJournalWriteResult later = await storage.MarkStepStartedAsync(
            journal,
            draft,
            1,
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionJournalWriteStatus.Invalid, later.Status);
        Assert.Equal(
            ExecutionJournalPersistenceFailureKind.JournalStateInvalid,
            later.FailureKind);

        ExecutionJournalReadResult loaded = await storage.LoadAsync(
            journal,
            draft,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            ExecutionStepOutcome.Failed,
            loaded.Snapshot!.Steps[0].Outcome);
        Assert.Equal(
            ExecutionStepOutcome.NotStarted,
            loaded.Snapshot.Steps[1].Outcome);
    }

    [Fact]
    public async Task DifferentDraftCannotReuseExistingJournal()
    {
        using var fixture = new InspectionFixture();
        ExecutionJournalDraft original = OneStepDraft();
        ExecutionJournalReference journal = await Create(fixture, original);
        ExecutionJournalDraft different = Draft(
            new ExecutionJournalEntry(0, "options.txt", ExpectedEntryKind.File, ExecutionOperationKind.Copy));

        ExecutionJournalReadResult loaded = await Store().LoadAsync(
            journal,
            different,
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionJournalReadStatus.Invalid, loaded.Status);
        Assert.Equal(
            ExecutionJournalPersistenceFailureKind.JournalFormatInvalid,
            loaded.FailureKind);
    }

    [Fact]
    public async Task ReparseJournalParentIsRejectedWithoutCreatingFileInTarget()
    {
        using var fixture = new InspectionFixture();
        Directory.CreateDirectory(fixture.At("outside"));
        fixture.Junction("journal-link", "outside");

        ExecutionJournalWriteResult result = await Store().CreateAsync(
            fixture.At("journal-link"),
            OneStepDraft(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionJournalWriteStatus.Invalid, result.Status);
        Assert.Equal(
            ExecutionJournalPersistenceFailureKind.ReparsePoint,
            result.FailureKind);
        Assert.Empty(Directory.EnumerateFiles(fixture.At("outside")));
    }

    [Fact]
    public async Task PreCancelledCreateLeavesNoJournalArtifact()
    {
        using var fixture = new InspectionFixture();
        string parent = fixture.At("journals");
        Directory.CreateDirectory(parent);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        ExecutionJournalWriteResult result = await Store().CreateAsync(
            parent,
            OneStepDraft(),
            cancellation.Token);

        Assert.Equal(ExecutionJournalWriteStatus.Cancelled, result.Status);
        Assert.Empty(Directory.EnumerateFiles(parent, "mim-journal-*.jsonl"));
    }

    private static WindowsExecutionJournalStorage Store() => new();

    private static ExecutionJournalDraft OneStepDraft() =>
        Draft(new ExecutionJournalEntry(
            0,
            "config",
            ExpectedEntryKind.Directory,
            ExecutionOperationKind.Replace));

    private static ExecutionJournalDraft Draft(params ExecutionJournalEntry[] entries) =>
        new(ExecutionJournalStatus.Ready, entries, []);

    private static async Task<ExecutionJournalReference> Create(
        InspectionFixture fixture,
        ExecutionJournalDraft draft)
    {
        string parent = fixture.At("journals");
        Directory.CreateDirectory(parent);
        ExecutionJournalWriteResult result = await Store().CreateAsync(
            parent,
            draft,
            TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        return Assert.IsType<ExecutionJournalReference>(result.Journal);
    }
}
