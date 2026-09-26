using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Domain.Planning;

public static class MigrationSelectionPresets
{
    private static readonly string[] RecommendedEntries = KnownEntryCatalog.All
        .Where(entry => entry.RecommendedByDefault)
        .Select(entry => entry.Name)
        .ToArray();

    public static IReadOnlyList<string> Recommended { get; } = Array.AsReadOnly(RecommendedEntries);
}
