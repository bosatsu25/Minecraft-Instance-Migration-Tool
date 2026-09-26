using MinecraftInstanceMigration.Tests;

namespace MinecraftInstanceMigration.Domain.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public async Task EvaluatedProjectKeepsInwardReferencesAndHasNoUi(string configuration)
    {
        await ArchitectureAssertions.ProjectRespectsBoundary("Domain", configuration);
    }

    [Fact]
    public void CompiledAssemblyKeepsInwardReferencesAndHasNoUi()
    {
        ArchitectureAssertions.AssemblyRespectsBoundary("Domain");
    }

    [Fact]
    public void DomainHasNoDirectFilesystemImplementationDependencies()
    {
        ArchitectureAssertions.DomainDoesNotReferenceFilesystemImplementations();
    }
}
