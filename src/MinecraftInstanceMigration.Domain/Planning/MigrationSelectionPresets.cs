using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Domain.Planning;

public static class MigrationSelectionPresets
{
    private static readonly string[] AllEntries = KnownEntryCatalog.All
        .Select(entry => entry.Name)
        .ToArray();

    private static readonly string[] RecommendedEntries = KnownEntryCatalog.All
        .Where(entry => entry.RecommendedByDefault)
        .Select(entry => entry.Name)
        .ToArray();

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(AllEntries);

    public static IReadOnlyList<string> Recommended { get; } = Array.AsReadOnly(RecommendedEntries);
}
