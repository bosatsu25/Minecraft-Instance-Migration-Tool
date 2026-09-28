using System.Reflection;
using System.Reflection.Emit;
using MinecraftInstanceMigration.App.Presentation;

namespace MinecraftInstanceMigration.App.Tests;

public sealed class ApplicationInformationTests
{
    [Fact]
    public void CurrentReadsProductAndVersionFromAssemblyMetadata()
    {
        ApplicationInformation information = ApplicationInformation.Current;

        Assert.Equal("Minecraft Instance Migration Tool", information.ProductName);
        Assert.Equal("0.9.0", information.Version);
        Assert.Equal("Windows x64", information.Platform);
        Assert.Equal("bosatsuKing", information.Author);
    }

    [Fact]
    public void FromAssemblyRejectsMissingRequiredMetadata()
    {
        Assembly assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("MetadataMissing"),
            AssemblyBuilderAccess.Run);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ApplicationInformation.FromAssembly(assembly));

        Assert.DoesNotContain("C:\\", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
