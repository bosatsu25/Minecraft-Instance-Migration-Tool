using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Planning;

public interface IMigrationPlanner
{
    MigrationPlan CreatePlan(
        InstanceInspectionResult source,
        InstanceInspectionResult destination,
        IEnumerable<string> selectedEntryNames);
}
