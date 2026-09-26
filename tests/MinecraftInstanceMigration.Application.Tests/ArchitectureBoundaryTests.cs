using MinecraftInstanceMigration.Tests;

namespace MinecraftInstanceMigration.Application.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public async Task EvaluatedProjectKeepsInwardReferencesAndHasNoUi(string configuration)
    {
        await ArchitectureAssertions.ProjectRespectsBoundary("Application", configuration, "Domain");
    }

    [Fact]
    public void CompiledAssemblyKeepsInwardReferencesAndHasNoUi()
    {
        ArchitectureAssertions.AssemblyRespectsBoundary("Application", "Domain");
    }
}
