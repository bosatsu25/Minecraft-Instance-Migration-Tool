using System.Runtime.Versioning;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Infrastructure.Execution;

namespace MinecraftInstanceMigration.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
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
}
