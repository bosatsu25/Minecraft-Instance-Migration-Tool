using MinecraftInstanceMigration.Domain.Rules;

namespace MinecraftInstanceMigration.Domain.Tests;

public sealed class KnownMigrationContentRulesTests
{
    [Theory]
    [InlineData("hanemod-client.json")]
    [InlineData("HANEMOD-CLIENT.JSON")]
    [InlineData("Hanemod-Client.Json")]
    public void HanemodClientFileNameUsesWindowsCaseSemantics(string fileName)
    {
        Assert.True(KnownMigrationContentRules.IsExcludedFileName(fileName));
    }

    [Theory]
    [InlineData("hanemod-client.json.bak")]
    [InlineData("my-hanemod-client.json")]
    [InlineData("hanemod-client")]
    public void SimilarNamesRemainMigrationContent(string fileName)
    {
        Assert.False(KnownMigrationContentRules.IsExcludedFileName(fileName));
    }
}
