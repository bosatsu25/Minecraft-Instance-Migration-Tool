namespace MinecraftInstanceMigration.Domain.Inspection;

public sealed record KnownEntryDefinition(string Name, ExpectedEntryKind ExpectedKind);

public static class KnownEntryCatalog
{
    private static readonly KnownEntryDefinition[] Entries =
    [
        new("options.txt", ExpectedEntryKind.File),
        new("config", ExpectedEntryKind.Directory),
        new("resourcepacks", ExpectedEntryKind.Directory),
        new("shaderpacks", ExpectedEntryKind.Directory),
        new("schematics", ExpectedEntryKind.Directory),
        new("saves", ExpectedEntryKind.Directory),
        new("screenshots", ExpectedEntryKind.Directory),
        new("XaeroWaypoints", ExpectedEntryKind.Directory),
        new("XaeroWorldMap", ExpectedEntryKind.Directory),
        new("itemscroller", ExpectedEntryKind.Directory),
        new("g4mespeed", ExpectedEntryKind.Directory),
    ];

    public static IReadOnlyList<KnownEntryDefinition> All { get; } = Array.AsReadOnly(Entries);
}
