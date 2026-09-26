namespace MinecraftInstanceMigration.Domain.Inspection;

public enum EntryState
{
    Missing,
    File,
    Directory,
    ReparsePoint,
    Inaccessible,
    InvalidPath,
    Unavailable,
}
