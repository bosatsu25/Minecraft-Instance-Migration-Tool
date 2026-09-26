using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Planning;

public sealed class MigrationPlanner : IMigrationPlanner
{
    public MigrationPlan CreatePlan(
        InstanceInspectionResult source,
        InstanceInspectionResult destination,
        IEnumerable<string> selectedEntryNames) =>
        MigrationPlanPolicy.Create(source, destination, selectedEntryNames);
}
