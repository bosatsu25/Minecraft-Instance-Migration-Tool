namespace MinecraftInstanceMigration.Domain.Inspection;

// How Merge combines an existing destination directory with the source (ADR 0004).
public enum MergeUnit
{
    NotOffered,

    // Each source file is written over the same relative path; destination-only content stays.
    File,

    // Each top-level child of the source replaces or joins the destination as a whole.
    TopLevelChild,
}

public sealed record KnownEntryDefinition(
    string Name,
    ExpectedEntryKind ExpectedKind,
    bool RecommendedByDefault,
    MergeUnit MergeUnit);

public static class KnownEntryCatalog
{
    private static readonly KnownEntryDefinition[] Entries =
    [
        new("options.txt", ExpectedEntryKind.File, true, MergeUnit.NotOffered),
        new("config", ExpectedEntryKind.Directory, true, MergeUnit.File),
        new("resourcepacks", ExpectedEntryKind.Directory, true, MergeUnit.TopLevelChild),
        new("shaderpacks", ExpectedEntryKind.Directory, true, MergeUnit.TopLevelChild),
        new("schematics", ExpectedEntryKind.Directory, true, MergeUnit.File),
        // A world is one unit; Merge stays unavailable until real-world use is reviewed (ADR 0004).
        new("saves", ExpectedEntryKind.Directory, false, MergeUnit.NotOffered),
        new("screenshots", ExpectedEntryKind.Directory, false, MergeUnit.File),
        new("XaeroWaypoints", ExpectedEntryKind.Directory, true, MergeUnit.TopLevelChild),
        new("XaeroWorldMap", ExpectedEntryKind.Directory, true, MergeUnit.TopLevelChild),
        new("itemscroller", ExpectedEntryKind.Directory, true, MergeUnit.File),
        new("g4mespeed", ExpectedEntryKind.Directory, true, MergeUnit.File),
    ];

    public static IReadOnlyList<KnownEntryDefinition> All { get; } = Array.AsReadOnly(Entries);
}
