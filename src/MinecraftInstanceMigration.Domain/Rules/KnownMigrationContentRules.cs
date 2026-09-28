namespace MinecraftInstanceMigration.Domain.Rules;

public static class KnownMigrationContentRules
{
    public const string HanemodClientFileName = "hanemod-client.json";

    public const string HanemodClientExclusionSummary =
        "hanemod-client.json files are excluded at every depth inside selected directories.";

    public static bool IsExcludedFileName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        return string.Equals(
            fileName,
            HanemodClientFileName,
            StringComparison.OrdinalIgnoreCase);
    }
}
