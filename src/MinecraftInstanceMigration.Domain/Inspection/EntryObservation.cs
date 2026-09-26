namespace MinecraftInstanceMigration.Domain.Inspection;

public enum ExpectedEntryKind
{
    File,
    Directory,
}

public sealed record EntryObservation(string Name, ExpectedEntryKind ExpectedKind, EntryState State)
{
    public bool? MatchesExpectedKind => State switch
    {
        EntryState.File => ExpectedKind == ExpectedEntryKind.File,
        EntryState.Directory => ExpectedKind == ExpectedEntryKind.Directory,
        _ => null,
    };
}
