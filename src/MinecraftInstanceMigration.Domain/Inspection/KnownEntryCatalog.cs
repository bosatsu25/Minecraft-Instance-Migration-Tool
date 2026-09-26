namespace MinecraftInstanceMigration.Domain.Inspection;

public sealed record KnownEntryDefinition(
    string Name,
    ExpectedEntryKind ExpectedKind,
    bool RecommendedByDefault);

public static class KnownEntryCatalog
{
    private static readonly KnownEntryDefinition[] Entries =
    [
        new("options.txt", ExpectedEntryKind.File, true),
        new("config", ExpectedEntryKind.Directory, true),
        new("resourcepacks", ExpectedEntryKind.Directory, true),
        new("shaderpacks", ExpectedEntryKind.Directory, true),
        new("schematics", ExpectedEntryKind.Directory, true),
        new("saves", ExpectedEntryKind.Directory, false),
        new("screenshots", ExpectedEntryKind.Directory, false),
        new("XaeroWaypoints", ExpectedEntryKind.Directory, true),
        new("XaeroWorldMap", ExpectedEntryKind.Directory, true),
        new("itemscroller", ExpectedEntryKind.Directory, true),
        new("g4mespeed", ExpectedEntryKind.Directory, true),
    ];

    public static IReadOnlyList<KnownEntryDefinition> All { get; } = Array.AsReadOnly(Entries);
}
