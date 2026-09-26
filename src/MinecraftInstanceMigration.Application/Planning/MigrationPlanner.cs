using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Planning;

public sealed class MigrationPlanner : IMigrationPlanner
{
    public MigrationPlan CreatePlan(
        InstanceInspectionResult source,
        InstanceInspectionResult destination,
        IEnumerable<string> selectedEntryNames,
        IReadOnlyDictionary<string, DestinationConflictDecision>? conflictDecisions = null) =>
        MigrationPlanPolicy.Create(source, destination, selectedEntryNames, conflictDecisions);

    public MigrationPlan CreateRecommendedPlan(
        InstanceInspectionResult source,
        InstanceInspectionResult destination,
        IReadOnlyDictionary<string, DestinationConflictDecision>? conflictDecisions = null) =>
        MigrationPlanPolicy.Create(source, destination, MigrationSelectionPresets.Recommended, conflictDecisions);
}
