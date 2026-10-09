using System.Diagnostics;
using System.Runtime.Versioning;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Infrastructure.Execution;

namespace MinecraftInstanceMigration.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
[Collection("Drive aliases")]
public sealed class ExecutionMutationIntegrationTests
{
    [Fact]
    public async Task CopyFileIsCreateOnlyAndVerifierMatchesSource()
    {
        using var fixture = new InspectionFixture();
        string source = fixture.At("source");
        string destination = fixture.At("destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(source, "options.txt"), "source-options");

        var step = new ExecutionJournalEntry(
            0,
            "options.txt",
            ExpectedEntryKind.File,
            ExecutionOperationKind.Copy);

        ExecutionMutationResult mutation =
            await new WindowsExecutionMutationPort().ApplyAsync(
                source,
                destination,
                step,
                TestContext.Current.CancellationToken);

        Assert.True(mutation.IsApplied);
        Assert.Equal(
            "source-options",
            File.ReadAllText(Path.Combine(destination, "options.txt")));
        Assert.Equal(
            "source-options",
            File.ReadAllText(Path.Combine(source, "options.txt")));

        ExecutionPostWriteVerificationResult verification =
            await new WindowsExecutionPostWriteVerifier().VerifyAsync(
                source,
                destination,
                step,
                TestContext.Current.CancellationToken);

        Assert.True(verification.IsVerified);
        Assert.NotNull(verification.Fingerprint);
        Assert.Equal(1, verification.Fingerprint.FileCount);
        Assert.Equal(0, verification.Fingerprint.DirectoryCount);
    }

    [Fact]
    public async Task CopyDirectoryExcludesHanemodClientAtEveryDepthAndVerifierUsesSameRule()
    {
        using var fixture = new InspectionFixture();
        string source = fixture.At("source");
        string destination = fixture.At("destination");
        Directory.CreateDirectory(Path.Combine(source, "config", "nested"));
        Directory.CreateDirectory(Path.Combine(
            source, "config", "directory-case", "hanemod-client.json"));
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(source, "config", "normal.json"), "normal");
        File.WriteAllText(Path.Combine(source, "config", "hanemod-client.json"), "excluded-root");
        File.WriteAllText(Path.Combine(source, "config", "nested", "HANEMOD-CLIENT.JSON"), "excluded-nested");
        File.WriteAllText(Path.Combine(source, "config", "nested", "hanemod-client.json.bak"), "included-similar");
        File.WriteAllText(Path.Combine(
            source,
            "config",
            "directory-case",
            "hanemod-client.json",
            "inside.txt"), "directory-is-included");

        var step = new ExecutionJournalEntry(
            0, "config", ExpectedEntryKind.Directory, ExecutionOperationKind.Copy);

        ExecutionMutationResult mutation = await new WindowsExecutionMutationPort().ApplyAsync(
            source, destination, step, TestContext.Current.CancellationToken);
        ExecutionPostWriteVerificationResult verification =
            await new WindowsExecutionPostWriteVerifier().VerifyAsync(
                source, destination, step, TestContext.Current.CancellationToken);

        Assert.True(mutation.IsApplied, $"{mutation.Status}/{mutation.FailureKind}");
        Assert.True(verification.IsVerified, $"{verification.Status}/{verification.FailureKind}");
        Assert.Equal("normal", File.ReadAllText(Path.Combine(destination, "config", "normal.json")));
        Assert.Equal("included-similar", File.ReadAllText(Path.Combine(
            destination, "config", "nested", "hanemod-client.json.bak")));
        Assert.Equal("directory-is-included", File.ReadAllText(Path.Combine(
            destination,
            "config",
            "directory-case",
            "hanemod-client.json",
            "inside.txt")));
        Assert.False(File.Exists(Path.Combine(destination, "config", "hanemod-client.json")));
        Assert.False(File.Exists(Path.Combine(destination, "config", "nested", "HANEMOD-CLIENT.JSON")));
    }

    [Fact]
    public async Task ReplacePreservesExistingExcludedFilesWhileReplacingOtherContent()
    {
        using var fixture = new InspectionFixture();
        string source = fixture.At("source");
        string destination = fixture.At("destination");
        Directory.CreateDirectory(Path.Combine(source, "config", "nested"));
        Directory.CreateDirectory(Path.Combine(destination, "config", "nested"));
        Directory.CreateDirectory(Path.Combine(destination, "config", "destination-only"));
        File.WriteAllText(Path.Combine(source, "config", "new.json"), "new");
        File.WriteAllText(Path.Combine(source, "config", "nested", "hanemod-client.json"), "source-excluded");
        File.WriteAllText(Path.Combine(destination, "config", "old.json"), "old");
        File.WriteAllText(Path.Combine(destination, "config", "nested", "HANEMOD-CLIENT.JSON"), "destination-preserved");
        File.WriteAllText(
            Path.Combine(destination, "config", "destination-only", "hanemod-client.json"),
            "destination-only-preserved");

        var step = new ExecutionJournalEntry(
            0, "config", ExpectedEntryKind.Directory, ExecutionOperationKind.Replace);

        ExecutionMutationResult mutation = await new WindowsExecutionMutationPort().ApplyAsync(
            source, destination, step, TestContext.Current.CancellationToken);
        ExecutionPostWriteVerificationResult verification =
            await new WindowsExecutionPostWriteVerifier().VerifyAsync(
                source, destination, step, TestContext.Current.CancellationToken);

        Assert.True(mutation.IsApplied, $"{mutation.Status}/{mutation.FailureKind}");
        Assert.True(verification.IsVerified, $"{verification.Status}/{verification.FailureKind}");
        Assert.Equal("new", File.ReadAllText(Path.Combine(destination, "config", "new.json")));
        Assert.False(File.Exists(Path.Combine(destination, "config", "old.json")));
        Assert.Equal("destination-preserved", File.ReadAllText(Path.Combine(
            destination, "config", "nested", "HANEMOD-CLIENT.JSON")));
        Assert.Equal("destination-only-preserved", File.ReadAllText(Path.Combine(
            destination, "config", "destination-only", "hanemod-client.json")));
    }

    [Fact]
    public async Task CopyRefusesExistingDestinationWithoutOverwrite()
    {
        using var fixture = new InspectionFixture();
        string source = fixture.At("source");
        string destination = fixture.At("destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(source, "options.txt"), "new");
        File.WriteAllText(Path.Combine(destination, "options.txt"), "existing");

        ExecutionMutationResult result =
            await new WindowsExecutionMutationPort().ApplyAsync(
                source,
                destination,
                new ExecutionJournalEntry(
                    0,
                    "options.txt",
                    ExpectedEntryKind.File,
                    ExecutionOperationKind.Copy),
                TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionMutationStatus.Failed, result.Status);
        Assert.Equal(
            ExecutionMutationFailureKind.DestinationChanged,
            result.FailureKind);
        Assert.Equal(
            "existing",
            File.ReadAllText(Path.Combine(destination, "options.txt")));
    }

    [Fact]
    public async Task NestedSourceJunctionFailsClosedAndTargetPayloadIsNotCopied()
    {
        using var fixture = new InspectionFixture();
        string source = fixture.At("source");
        string destination = fixture.At("destination");
        Directory.CreateDirectory(Path.Combine(source, "config"));
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(fixture.At("outside"));
        File.WriteAllText(fixture.At("outside/secret.txt"), "must-not-copy");
        fixture.Junction("source/config/00-linked", "outside");

        ExecutionMutationResult result =
            await new WindowsExecutionMutationPort().ApplyAsync(
                source,
                destination,
                new ExecutionJournalEntry(
                    0,
                    "config",
                    ExpectedEntryKind.Directory,
                    ExecutionOperationKind.Copy),
                TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionMutationStatus.Failed, result.Status);
        Assert.Equal(ExecutionMutationFailureKind.ReparsePoint, result.FailureKind);
        Assert.Equal("must-not-copy", File.ReadAllText(fixture.At("outside/secret.txt")));

        if (Directory.Exists(Path.Combine(destination, "config")))
        {
            Assert.DoesNotContain(
                Directory.EnumerateFiles(
                    Path.Combine(destination, "config"),
                    "*",
                    SearchOption.AllDirectories),
                path => File.ReadAllText(path).Contains(
                    "must-not-copy",
                    StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task ReplaceDirectoryRemovesOldTreeAndCopiesSource()
    {
        using var fixture = new InspectionFixture();
        string source = fixture.At("source");
        string destination = fixture.At("destination");
        Directory.CreateDirectory(Path.Combine(source, "config", "nested"));
        Directory.CreateDirectory(Path.Combine(destination, "config", "legacy"));
        File.WriteAllText(Path.Combine(source, "config", "nested", "new.json"), "new-value");
        File.WriteAllText(Path.Combine(destination, "config", "legacy", "old.json"), "old-value");

        var step = new ExecutionJournalEntry(
            0,
            "config",
            ExpectedEntryKind.Directory,
            ExecutionOperationKind.Replace);

        ExecutionMutationResult mutation =
            await new WindowsExecutionMutationPort().ApplyAsync(
                source,
                destination,
                step,
                TestContext.Current.CancellationToken);

        Assert.True(mutation.IsApplied);
        Assert.False(Directory.Exists(Path.Combine(destination, "config", "legacy")));
        Assert.Equal(
            "new-value",
            File.ReadAllText(Path.Combine(destination, "config", "nested", "new.json")));

        ExecutionPostWriteVerificationResult verification =
            await new WindowsExecutionPostWriteVerifier().VerifyAsync(
                source,
                destination,
                step,
                TestContext.Current.CancellationToken);

        Assert.True(verification.IsVerified);
    }

    [Fact]
    public async Task ReplaceRejectsNestedDestinationJunctionWithoutFollowingTarget()
    {
        using var fixture = new InspectionFixture();
        string source = fixture.At("source");
        string destination = fixture.At("destination");
        Directory.CreateDirectory(Path.Combine(source, "config"));
        Directory.CreateDirectory(Path.Combine(destination, "config"));
        Directory.CreateDirectory(fixture.At("outside"));
        File.WriteAllText(Path.Combine(source, "config", "new.txt"), "new");
        File.WriteAllText(fixture.At("outside/secret.txt"), "keep-me");
        fixture.Junction("destination/config/00-linked", "outside");

        ExecutionMutationResult result =
            await new WindowsExecutionMutationPort().ApplyAsync(
                source,
                destination,
                new ExecutionJournalEntry(
                    0,
                    "config",
                    ExpectedEntryKind.Directory,
                    ExecutionOperationKind.Replace),
                TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionMutationStatus.Failed, result.Status);
        Assert.Equal(ExecutionMutationFailureKind.ReparsePoint, result.FailureKind);
        Assert.Equal("keep-me", File.ReadAllText(fixture.At("outside/secret.txt")));
    }

    [Fact]
    public async Task OverlappingRootsAreRejectedBeforeMutation()
    {
        using var fixture = new InspectionFixture();
        string source = fixture.At("source");
        string destination = Path.Combine(source, "nested-destination");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(source, "options.txt"), "source");

        ExecutionMutationResult result =
            await new WindowsExecutionMutationPort().ApplyAsync(
                source,
                destination,
                new ExecutionJournalEntry(
                    0,
                    "options.txt",
                    ExpectedEntryKind.File,
                    ExecutionOperationKind.Copy),
                TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionMutationStatus.Failed, result.Status);
        Assert.Equal(
            ExecutionMutationFailureKind.OverlappingRoots,
            result.FailureKind);
        Assert.False(File.Exists(Path.Combine(destination, "options.txt")));
    }


    [Fact]
    public async Task MissingSourceRootIsClassifiedAsSourceChanged()
    {
        using var fixture = new InspectionFixture();
        string source = fixture.At("missing-source");
        string destination = fixture.At("destination");
        Directory.CreateDirectory(destination);

        ExecutionMutationResult result =
            await new WindowsExecutionMutationPort().ApplyAsync(
                source,
                destination,
                new ExecutionJournalEntry(
                    0,
                    "options.txt",
                    ExpectedEntryKind.File,
                    ExecutionOperationKind.Copy),
                TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionMutationStatus.Failed, result.Status);
        Assert.Equal(
            ExecutionMutationFailureKind.SourceChanged,
            result.FailureKind);
    }

    [Fact]
    public async Task MissingDestinationRootIsClassifiedAsDestinationChanged()
    {
        using var fixture = new InspectionFixture();
        string source = fixture.At("source");
        string destination = fixture.At("missing-destination");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "options.txt"), "source");

        ExecutionMutationResult result =
            await new WindowsExecutionMutationPort().ApplyAsync(
                source,
                destination,
                new ExecutionJournalEntry(
                    0,
                    "options.txt",
                    ExpectedEntryKind.File,
                    ExecutionOperationKind.Copy),
                TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionMutationStatus.Failed, result.Status);
        Assert.Equal(
            ExecutionMutationFailureKind.DestinationChanged,
            result.FailureKind);
    }

    [Fact]
    public async Task PhysicalAliasOverlapIsRejectedBeforeMutation()
    {
        using var fixture = new InspectionFixture();
        string source = fixture.At("source");
        Directory.CreateDirectory(Path.Combine(source, "nested-destination"));
        File.WriteAllText(Path.Combine(source, "options.txt"), "source");

        char drive = CreateSubst(source);
        try
        {
            string destination = $"{drive}:\\nested-destination";

            ExecutionMutationResult result =
                await new WindowsExecutionMutationPort().ApplyAsync(
                    source,
                    destination,
                    new ExecutionJournalEntry(
                        0,
                        "options.txt",
                        ExpectedEntryKind.File,
                        ExecutionOperationKind.Copy),
                    TestContext.Current.CancellationToken);

            Assert.Equal(ExecutionMutationStatus.Failed, result.Status);
            Assert.Equal(
                ExecutionMutationFailureKind.OverlappingRoots,
                result.FailureKind);
            Assert.False(File.Exists(Path.Combine(destination, "options.txt")));
        }
        finally
        {
            RemoveSubst(drive);
        }
    }

    [Fact]
    public async Task IndependentVerifierDetectsDestinationTampering()
    {
        using var fixture = new InspectionFixture();
        string source = fixture.At("source");
        string destination = fixture.At("destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(source, "options.txt"), "expected");

        var step = new ExecutionJournalEntry(
            0,
            "options.txt",
            ExpectedEntryKind.File,
            ExecutionOperationKind.Copy);

        Assert.True((await new WindowsExecutionMutationPort().ApplyAsync(
            source,
            destination,
            step,
            TestContext.Current.CancellationToken)).IsApplied);

        File.WriteAllText(Path.Combine(destination, "options.txt"), "tampered");

        ExecutionPostWriteVerificationResult verification =
            await new WindowsExecutionPostWriteVerifier().VerifyAsync(
                source,
                destination,
                step,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ExecutionPostWriteVerificationStatus.Failed,
            verification.Status);
        Assert.Equal(
            ExecutionPostWriteVerificationFailureKind.VerificationMismatch,
            verification.FailureKind);
    }


    private static char CreateSubst(string target)
    {
        var used = DriveInfo.GetDrives()
            .Select(drive => char.ToUpperInvariant(drive.Name[0]))
            .ToHashSet();

        char drive = Enumerable.Range('R', 'Z' - 'R' + 1)
            .Select(value => (char)value)
            .Reverse()
            .First(candidate => !used.Contains(candidate));

        RunSubst($"{drive}:", target);
        return drive;
    }

    private static void RemoveSubst(char drive) =>
        RunSubst($"{drive}:", "/D");

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

        using Process process = Process.Start(start)!;
        Assert.True(process.WaitForExit(10000), "SUBST command timed out.");
        Assert.Equal(0, process.ExitCode);
    }
}
