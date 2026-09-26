using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Application.Inspection;

public interface IInstanceInspector
{
    Task<InstanceInspectionResult> InspectAsync(string candidatePath, CancellationToken cancellationToken = default);
}
