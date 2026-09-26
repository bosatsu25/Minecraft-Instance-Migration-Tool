using System.Runtime.Versioning;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Infrastructure.Execution;

namespace MinecraftInstanceMigration.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public sealed class ExecutionWorkspaceSafetyTests
{
    [Fact]
    public async Task JournalInsideSourceRootIsRejected()
    {
        using var fixture = new InspectionFixture();
        string source = fixture.At("source");
        string destination = fixture.At("destination");
        string journal = fixture.At("source/journals");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(journal);

        ExecutionWorkspaceSafetyResult result =
            await new WindowsExecutionWorkspaceSafetyValidator().ValidateAsync(
                source,
                destination,
                journal,
                TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionWorkspaceSafetyStatus.Invalid, result.Status);
        Assert.Equal(
            ExecutionWorkspaceSafetyFailureKind.JournalInsideMigrationRoot,
            result.FailureKind);
    }

    [Fact]
    public async Task JournalInsideDestinationRootIsRejected()
    {
        using var fixture = new InspectionFixture();
        string source = fixture.At("source");
        string destination = fixture.At("destination");
        string journal = fixture.At("destination/config/journals");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(journal);

        ExecutionWorkspaceSafetyResult result =
            await new WindowsExecutionWorkspaceSafetyValidator().ValidateAsync(
                source,
                destination,
                journal,
                TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionWorkspaceSafetyStatus.Invalid, result.Status);
        Assert.Equal(
            ExecutionWorkspaceSafetyFailureKind.JournalInsideMigrationRoot,
            result.FailureKind);
    }

    [Fact]
    public async Task SeparateOwnedRootsAndJournalAreSafe()
    {
        using var fixture = new InspectionFixture();
        string source = fixture.At("source");
        string destination = fixture.At("destination");
        string journal = fixture.At("journals");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(journal);

        ExecutionWorkspaceSafetyResult result =
            await new WindowsExecutionWorkspaceSafetyValidator().ValidateAsync(
                source,
                destination,
                journal,
                TestContext.Current.CancellationToken);

        Assert.True(result.IsSafe);
    }
}
