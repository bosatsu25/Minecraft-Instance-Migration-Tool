using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Application.Inspection;

public sealed class InstanceInspector(IInspectionFileSystem fileSystem) : IInstanceInspector
{
    private readonly IInspectionFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

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

        var entries = new List<EntryObservation>(KnownEntryCatalog.All.Count);
        foreach (KnownEntryDefinition target in KnownEntryCatalog.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = session.ObserveChild(target.Name, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(new EntryObservation(target.Name, target.ExpectedKind, state));
        }

        return new InstanceInspectionResult(session.RootState, entries);
    }
}
