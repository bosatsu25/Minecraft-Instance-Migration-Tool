using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Planning;

// Read-only comparison of one entry's source and destination trees by its catalog merge unit
// (ADR 0004). The result carries counts only, never paths.
public interface IMergeInspector
{
    Task<MergeInspectionResult> InspectAsync(
        string sourceRoot,
        string destinationRoot,
        string entryName,
        CancellationToken cancellationToken = default);
}
