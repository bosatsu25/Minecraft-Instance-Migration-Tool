using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
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

    [Theory]
    [InlineData("C:\\private\\options.txt")]
    [InlineData("../private")]
    [InlineData("config/secret")]
    [InlineData("options.txt:secret")]
    [InlineData("")]
    public async Task PathShapedNamesNeverCreateAnArtifact(string name)
    {
        using var fixture = new InspectionFixture();
        Directory.CreateDirectory(fixture.At("destination"));
        Directory.CreateDirectory(fixture.At("journals"));
        var plan = new RollbackPlan(RollbackPlanStatus.Ready,
            [OneActionPlan().Entries[0] with { Name = name }], []);

        var result = await Store().CreateAsync(fixture.At("journals"),
            fixture.At("destination"), null, plan, TestContext.Current.CancellationToken);

        Assert.Equal(RollbackAttemptWriteStatus.Invalid, result.Status);
        Assert.Equal(RollbackAttemptPersistenceFailureKind.InvalidPlan, result.FailureKind);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.At("journals")));
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("checksum")]
    [InlineData("previous")]
    [InlineData("index")]
    [InlineData("duplicate")]
    [InlineData("reordered")]
    [InlineData("null-entry")]
    [InlineData("unknown-field")]
    [InlineData("duplicate-property")]
    [InlineData("missing-property")]
    public async Task CompleteCorruptionFailsClosedWithoutTruncation(string corruption)
    {
        using var fixture = new InspectionFixture();
        RollbackPlan plan = OneActionPlan();
        var attempt = await Create(fixture, plan);
        Assert.True((await Store().MarkActionStartedAsync(attempt, plan, 0,
            TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await Store().MarkActionAppliedAsync(attempt, plan, 0,
            TestContext.Current.CancellationToken)).IsSuccess);
        var lines = File.ReadAllLines(attempt.JournalPath).ToList();
        switch (corruption)
        {
            case "malformed": lines.Add("{"); break;
            case "checksum":
                lines[1] = Rewrite(lines[1], node => node["kind"] = "Applied", false);
                break;
            case "previous":
                lines[1] = Rewrite(lines[1], node => node["previousChecksum"] = new string('A', 64));
                break;
            case "index": lines[1] = Rewrite(lines[1], node => node["index"] = 8); break;
            case "duplicate": lines.Insert(2, lines[1]); break;
            case "reordered": (lines[1], lines[2]) = (lines[2], lines[1]); break;
            case "null-entry":
                lines = [Rewrite(lines[0], node => node["entries"]![0] = null)];
                break;
            case "unknown-field":
                lines[0] = lines[0].Insert(1, "\"unexpected\":true,");
                break;
            case "duplicate-property":
                lines[1] = lines[1].Replace("\"kind\":\"Started\"",
                    "\"kind\":\"Applied\",\"kind\":\"Started\"", StringComparison.Ordinal);
                break;
            case "missing-property":
                lines[0] = lines[0].Replace("\"index\":0,", "", StringComparison.Ordinal);
                break;
        }

        File.WriteAllText(attempt.JournalPath, string.Join('\n', lines) + "\n");
        byte[] corruptBytes = File.ReadAllBytes(attempt.JournalPath);
        var loaded = await Store().LoadAsync(attempt, plan, TestContext.Current.CancellationToken);
        Assert.Equal(RollbackAttemptReadStatus.Invalid, loaded.Status);
        var append = await Store().MarkActionFailedAsync(attempt, plan, 0,
            RollbackStorageFailureKind.IoFailure, TestContext.Current.CancellationToken);
        Assert.Equal(RollbackAttemptWriteStatus.Invalid, append.Status);
        Assert.Equal(corruptBytes, File.ReadAllBytes(attempt.JournalPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialAppliedAttemptRequiresRecoveryAfterReload(bool rejected)
    {
        using var fixture = new InspectionFixture();
        var plan = TwoActionPlan();
        var attempt = await Create(fixture, plan);
        Assert.True((await Store().MarkActionStartedAsync(attempt, plan, 0,
            TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await Store().MarkActionAppliedAsync(attempt, plan, 0,
            TestContext.Current.CancellationToken)).IsSuccess);
        if (rejected)
        {
            Assert.True((await Store().MarkActionStartedAsync(attempt, plan, 1,
                TestContext.Current.CancellationToken)).IsSuccess);
            Assert.True((await Store().MarkActionGuardRejectedAsync(attempt, plan, 1,
                RollbackStorageFailureKind.DestinationChanged,
                TestContext.Current.CancellationToken)).IsSuccess);
        }

        var loaded = await Store().LoadAsync(attempt, plan, TestContext.Current.CancellationToken);
        Assert.True(loaded.IsLoaded);
        Assert.True(loaded.Snapshot!.RequiresRecovery);
        Assert.Equal(RollbackAttemptStepOutcome.Applied, loaded.Snapshot.Steps[0].Outcome);
        Assert.Equal(rejected ? RollbackAttemptStepOutcome.GuardRejected : RollbackAttemptStepOutcome.NotStarted,
            loaded.Snapshot.Steps[1].Outcome);
    }

    [Fact]
    public async Task DuplicateAndOutOfOrderTransitionsDoNotChangeBytes()
    {
        using var fixture = new InspectionFixture();
        var plan = OneActionPlan();
        var attempt = await Create(fixture, plan);
        var token = TestContext.Current.CancellationToken;
        byte[] header = File.ReadAllBytes(attempt.JournalPath);
        Assert.False((await Store().MarkActionAppliedAsync(attempt, plan, 0, token)).IsSuccess);
        Assert.Equal(header, File.ReadAllBytes(attempt.JournalPath));
        Assert.True((await Store().MarkActionStartedAsync(attempt, plan, 0, token)).IsSuccess);
        byte[] started = File.ReadAllBytes(attempt.JournalPath);
        Assert.False((await Store().MarkActionStartedAsync(attempt, plan, 0, token)).IsSuccess);
        Assert.Equal(started, File.ReadAllBytes(attempt.JournalPath));
        Assert.True((await Store().MarkActionAppliedAsync(attempt, plan, 0, token)).IsSuccess);
        byte[] applied = File.ReadAllBytes(attempt.JournalPath);
        Assert.False((await Store().MarkActionAppliedAsync(attempt, plan, 0, token)).IsSuccess);
        Assert.False((await Store().MarkActionStartedAsync(attempt, plan, 0, token)).IsSuccess);
        Assert.Equal(applied, File.ReadAllBytes(attempt.JournalPath));
    }

    [Fact]
    public async Task EveryTruncatedFinalRecordRecoversOnlyThePriorCompleteState()
    {
        using var fixture = new InspectionFixture();
        var plan = OneActionPlan();
        var attempt = await Create(fixture, plan);
        var token = TestContext.Current.CancellationToken;
        Assert.True((await Store().MarkActionStartedAsync(attempt, plan, 0, token)).IsSuccess);
        Assert.True((await Store().MarkActionAppliedAsync(attempt, plan, 0, token)).IsSuccess);
        string[] records = File.ReadAllLines(attempt.JournalPath);
        string prefix = "";
        for (int record = 0; record < records.Length; record++)
        {
            for (int length = 0; length <= records[record].Length; length++)
            {
                File.WriteAllText(attempt.JournalPath, prefix + records[record][..length]);
                var loaded = await Store().LoadAsync(attempt, plan, token);
                if (record == 0)
                {
                    Assert.Equal(RollbackAttemptReadStatus.Invalid, loaded.Status);
                }
                else
                {
                    Assert.True(loaded.IsLoaded);
                    Assert.Equal(record == 1 ? RollbackAttemptStepOutcome.NotStarted : RollbackAttemptStepOutcome.Uncertain,
                        Assert.Single(loaded.Snapshot!.Steps).Outcome);
                }
            }

            prefix += records[record] + "\n";
        }
    }

    private static string Rewrite(string line, Action<JsonNode> change, bool rehash = true)
    {
        JsonNode envelope = JsonNode.Parse(line)!;
        JsonNode payload = envelope["payload"]!;
        change(payload);
        if (rehash)
        {
            envelope["checksum"] = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(payload.ToJsonString())));
        }

        return envelope.ToJsonString();
    }

    [Theory]
    [InlineData("name")]
    [InlineData("kind")]
    [InlineData("operation-action")]
    [InlineData("fingerprint")]
    [InlineData("order")]
    public async Task EverySafetyFieldIsBoundToTheHeader(string field)
    {
        using var fixture = new InspectionFixture();
        var plan = TwoActionPlan();
        var attempt = await Create(fixture, plan);
        RollbackPlanEntry[] entries = plan.Entries.ToArray();
        entries[0] = field switch
        {
            "name" => entries[0] with { Name = "saves" },
            "kind" => entries[0] with { ExpectedKind = ExpectedEntryKind.File },
            "operation-action" => entries[0] with
            {
                Operation = ExecutionOperationKind.Copy,
                Action = RollbackActionKind.DeleteCreatedEntry,
            },
            "fingerprint" => entries[0] with { ExpectedCurrentFingerprint = Fingerprint('F') },
            _ => entries[0],
        };
        if (field == "order")
        {
            entries = [entries[1] with { Order = 0 }, entries[0] with { Order = 1 }];
        }

        var different = new RollbackPlan(RollbackPlanStatus.Ready, entries, []);
        var loaded = await Store().LoadAsync(attempt, different, TestContext.Current.CancellationToken);
        Assert.Equal(RollbackAttemptReadStatus.Invalid, loaded.Status);
        Assert.Equal(RollbackAttemptPersistenceFailureKind.JournalFormatInvalid, loaded.FailureKind);
        Assert.False((await Store().MarkActionStartedAsync(attempt, different, 0,
            TestContext.Current.CancellationToken)).IsSuccess);
    }

    [Fact]
    public async Task JournalInsideBackupIsRejectedWithoutArtifact()
    {
        using var fixture = new InspectionFixture();
        Directory.CreateDirectory(fixture.At("destination"));
        Directory.CreateDirectory(fixture.At("backup/journals"));
        var result = await Store().CreateAsync(fixture.At("backup/journals"),
            fixture.At("destination"), fixture.At("backup"), OneActionPlan(),
            TestContext.Current.CancellationToken);
        Assert.Equal(RollbackAttemptPersistenceFailureKind.UnsafeWorkspace, result.FailureKind);
        Assert.Empty(Directory.EnumerateFiles(fixture.At("backup/journals")));
    }

    [Fact]
    public async Task ReparseJournalParentAndReplacementFailClosed()
    {
        using var fixture = new InspectionFixture();
        var plan = OneActionPlan();
        var attempt = await Create(fixture, plan);
        Directory.CreateDirectory(fixture.At("outside"));
        Directory.Move(fixture.At("journals"), fixture.At("retained-journals"));
        fixture.Junction("journals", "outside");
        var created = await Store().CreateAsync(fixture.At("journals"),
            fixture.At("destination"), null, plan, TestContext.Current.CancellationToken);
        var loaded = await Store().LoadAsync(attempt, plan, TestContext.Current.CancellationToken);
        var started = await Store().MarkActionStartedAsync(attempt, plan, 0,
            TestContext.Current.CancellationToken);
        Assert.Equal(RollbackAttemptPersistenceFailureKind.ReparsePoint, created.FailureKind);
        Assert.Equal(RollbackAttemptPersistenceFailureKind.ReparsePoint, loaded.FailureKind);
        Assert.Equal(RollbackAttemptPersistenceFailureKind.ReparsePoint, started.FailureKind);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.At("outside")));
        Assert.Single(File.ReadAllLines(Directory.GetFiles(fixture.At("retained-journals")).Single()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubstAliasCannotPlaceJournalInsideProtectedRoot(bool insideBackup)
    {
        using var fixture = new InspectionFixture();
        Directory.CreateDirectory(fixture.At("destination/journals"));
        Directory.CreateDirectory(fixture.At("backup/journals"));
        var used = DriveInfo.GetDrives().Select(drive => char.ToUpperInvariant(drive.Name[0])).ToHashSet();
        // Other integration fixtures reserve R-Z; use a separate available range.
        char alias = Enumerable.Range('M', 'Q' - 'M' + 1).Select(value => (char)value)
            .First(candidate => !used.Contains(candidate));
        RunSubst($"{alias}:", fixture.Root);
        try
        {
            string protectedName = insideBackup ? "backup" : "destination";
            var result = await Store().CreateAsync($"{alias}:\\{protectedName}\\journals",
                fixture.At("destination"), fixture.At("backup"), OneActionPlan(),
                TestContext.Current.CancellationToken);
            Assert.Equal(RollbackAttemptWriteStatus.Invalid, result.Status);
            Assert.Equal(RollbackAttemptPersistenceFailureKind.UnsafeWorkspace, result.FailureKind);
            Assert.Empty(Directory.EnumerateFiles(fixture.At(protectedName + "/journals")));
        }
        finally
        {
            RunSubst($"{alias}:", "/D");
        }
    }

    private static void RunSubst(string drive, string argument)
    {
        var start = new ProcessStartInfo("subst.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(drive);
        start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        if (!process.WaitForExit(10000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Assert.Fail("Fixture SUBST operation timed out.");
        }

        Assert.Equal(0, process.ExitCode);
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
