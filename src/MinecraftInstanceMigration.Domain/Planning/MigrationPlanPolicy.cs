using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Domain.Planning;

public static class MigrationPlanPolicy
{
    public static MigrationPlan Create(
        InstanceInspectionResult source,
        InstanceInspectionResult destination,
        IEnumerable<string> selectedEntryNames)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(selectedEntryNames);

        string[] requested = selectedEntryNames.ToArray();
        var knownNames = KnownEntryCatalog.All
            .Select(candidate => candidate.Name)
            .ToHashSet(StringComparer.Ordinal);
        var selected = requested
            .Where(knownNames.Contains)
            .ToHashSet(StringComparer.Ordinal);
        string[] unknown = requested
            .Where(name => !knownNames.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var entries = KnownEntryCatalog.All
            .Select(candidate => CreateEntry(source, destination, candidate, selected.Contains(candidate.Name)))
            .ToArray();

        return new MigrationPlan(source.RootState, destination.RootState, entries, unknown);
    }

    private static MigrationPlanEntry CreateEntry(
        InstanceInspectionResult source,
        InstanceInspectionResult destination,
        KnownEntryDefinition candidate,
        bool selected)
    {
        EntryObservation? sourceObservation = FindUniqueObservation(source, candidate.Name);
        EntryObservation? destinationObservation = FindUniqueObservation(destination, candidate.Name);

        if (!selected)
        {
            return new MigrationPlanEntry(
                candidate.Name,
                candidate.ExpectedKind,
                sourceObservation?.State,
                destinationObservation?.State,
                false,
                MigrationPlanDisposition.ExcludedBySelection);
        }

        if (source.RootState != EntryState.Directory)
        {
            return Entry(candidate, sourceObservation, destinationObservation, MigrationPlanDisposition.BlockedSourceRoot);
        }

        if (destination.RootState != EntryState.Directory)
        {
            return Entry(candidate, sourceObservation, destinationObservation, MigrationPlanDisposition.BlockedDestinationRoot);
        }

        if (sourceObservation is null)
        {
            return Entry(candidate, null, destinationObservation, MigrationPlanDisposition.BlockedSourceObservation);
        }

        if (sourceObservation.State == EntryState.Missing)
        {
            return Entry(candidate, sourceObservation, destinationObservation, MigrationPlanDisposition.SourceMissing);
        }

        if (sourceObservation.State == EntryState.ReparsePoint)
        {
            return Entry(candidate, sourceObservation, destinationObservation, MigrationPlanDisposition.BlockedSourceReparsePoint);
        }

        if (sourceObservation.State is EntryState.Inaccessible or EntryState.InvalidPath or EntryState.Unavailable)
        {
            return Entry(candidate, sourceObservation, destinationObservation, MigrationPlanDisposition.BlockedSourceObservation);
        }

        if (sourceObservation.MatchesExpectedKind != true)
        {
            return Entry(candidate, sourceObservation, destinationObservation, MigrationPlanDisposition.BlockedSourceKindMismatch);
        }

        if (destinationObservation is null)
        {
            return Entry(candidate, sourceObservation, null, MigrationPlanDisposition.BlockedDestinationObservation);
        }

        return destinationObservation.State switch
        {
            EntryState.Missing =>
                Entry(candidate, sourceObservation, destinationObservation, MigrationPlanDisposition.ReadyToCopy),
            EntryState.ReparsePoint =>
                Entry(candidate, sourceObservation, destinationObservation, MigrationPlanDisposition.BlockedDestinationReparsePoint),
            EntryState.Inaccessible or EntryState.InvalidPath or EntryState.Unavailable =>
                Entry(candidate, sourceObservation, destinationObservation, MigrationPlanDisposition.BlockedDestinationObservation),
            EntryState.File or EntryState.Directory =>
                Entry(candidate, sourceObservation, destinationObservation, MigrationPlanDisposition.DestinationConflict),
            _ => Entry(candidate, sourceObservation, destinationObservation, MigrationPlanDisposition.BlockedDestinationObservation),
        };
    }

    private static MigrationPlanEntry Entry(
        KnownEntryDefinition candidate,
        EntryObservation? source,
        EntryObservation? destination,
        MigrationPlanDisposition disposition) =>
        new(candidate.Name, candidate.ExpectedKind, source?.State, destination?.State, true, disposition);

    private static EntryObservation? FindUniqueObservation(InstanceInspectionResult inspection, string name)
    {
        EntryObservation[] matches = inspection.Entries
            .Where(entry => string.Equals(entry.Name, name, StringComparison.Ordinal))
            .Take(2)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }
}
