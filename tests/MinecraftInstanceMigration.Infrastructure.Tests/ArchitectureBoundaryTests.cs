using MinecraftInstanceMigration.Tests;

namespace MinecraftInstanceMigration.Infrastructure.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public async Task EvaluatedProjectKeepsInwardReferencesAndHasNoUi(string configuration)
    {
        await ArchitectureAssertions.ProjectRespectsBoundary("Infrastructure", configuration, "Application", "Domain");
    }

    [Fact]
    public void CompiledAssemblyKeepsInwardReferencesAndHasNoUi()
    {
        ArchitectureAssertions.AssemblyRespectsBoundary("Infrastructure", "Application", "Domain");
    }
}
