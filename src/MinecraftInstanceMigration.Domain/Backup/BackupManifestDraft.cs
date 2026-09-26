using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Domain.Backup;

public sealed record BackupManifestEntryDraft(
    string Name,
    ExpectedEntryKind ExpectedKind,
    EntryState DestinationState);

public sealed class BackupManifestDraft
{
    public const int CurrentSchemaVersion = 1;

    public BackupManifestDraft(IEnumerable<BackupManifestEntryDraft> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Entries = Array.AsReadOnly(entries.ToArray());
    }

    public int SchemaVersion => CurrentSchemaVersion;

    public IReadOnlyList<BackupManifestEntryDraft> Entries { get; }
}
