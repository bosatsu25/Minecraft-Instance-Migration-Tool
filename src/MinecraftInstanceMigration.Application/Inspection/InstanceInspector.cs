using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Application.Inspection;

public sealed class InstanceInspector(IInspectionFileSystem fileSystem) : IInstanceInspector
{
    private readonly IInspectionFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    private static readonly (string Name, ExpectedEntryKind Kind)[] Targets =
    [
        ("options.txt", ExpectedEntryKind.File),
        ("config", ExpectedEntryKind.Directory),
        ("resourcepacks", ExpectedEntryKind.Directory),
        ("shaderpacks", ExpectedEntryKind.Directory),
        ("schematics", ExpectedEntryKind.Directory),
        ("saves", ExpectedEntryKind.Directory),
        ("screenshots", ExpectedEntryKind.Directory),
        ("XaeroWaypoints", ExpectedEntryKind.Directory),
        ("XaeroWorldMap", ExpectedEntryKind.Directory),
        ("itemscroller", ExpectedEntryKind.Directory),
        ("g4mespeed", ExpectedEntryKind.Directory),
    ];

    public Task<InstanceInspectionResult> InspectAsync(string candidatePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Inspect(candidatePath, cancellationToken), cancellationToken);
    }

    private InstanceInspectionResult Inspect(string candidatePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var session = _fileSystem.OpenRoot(candidatePath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (session.RootState != EntryState.Directory)
        {
            return new InstanceInspectionResult(session.RootState, []);
        }

        var entries = new List<EntryObservation>(Targets.Length);
        foreach (var (name, kind) in Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = session.ObserveChild(name, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(new EntryObservation(name, kind, state));
        }

        return new InstanceInspectionResult(session.RootState, entries);
    }
}
