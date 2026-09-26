namespace MinecraftInstanceMigration.Domain.Inspection;

public enum KnownEntriesState
{
    NotInspected,
    NoneObserved,
    Present,
    Indeterminate,
}

public sealed class InstanceInspectionResult
{
    public InstanceInspectionResult(EntryState rootState, IEnumerable<EntryObservation> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        RootState = rootState;
        Entries = Array.AsReadOnly(entries.ToArray());
    }

    public EntryState RootState { get; }

    public IReadOnlyList<EntryObservation> Entries { get; }

    public bool IsComplete => RootState == EntryState.Directory
        && Entries.All(entry => entry.State is EntryState.Missing or EntryState.File
            or EntryState.Directory or EntryState.ReparsePoint);

    public KnownEntriesState KnownEntries => RootState != EntryState.Directory
        ? KnownEntriesState.NotInspected
        : Entries.Any(entry => entry.State is EntryState.File or EntryState.Directory or EntryState.ReparsePoint)
            ? KnownEntriesState.Present
            : IsComplete ? KnownEntriesState.NoneObserved : KnownEntriesState.Indeterminate;
}
