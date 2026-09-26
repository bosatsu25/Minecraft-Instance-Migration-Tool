using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Application.Inspection;

/// <summary>Opens an observation session without creating or modifying the root.</summary>
public interface IInspectionFileSystem
{
    IInspectionSession OpenRoot(string candidatePath, CancellationToken cancellationToken);
}

/// <summary>Owns the root's read-only resources until inspection completes.</summary>
public interface IInspectionSession : IDisposable
{
    EntryState RootState { get; }

    EntryState ObserveChild(string childName, CancellationToken cancellationToken);
}
