using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Planning;

public interface IMigrationPlanner
{
    MigrationPlan CreatePlan(
        InstanceInspectionResult source,
        InstanceInspectionResult destination,
        IEnumerable<string> selectedEntryNames,
        IReadOnlyDictionary<string, DestinationConflictDecision>? conflictDecisions = null,
        IReadOnlyDictionary<string, MergeInspectionResult>? mergeInspections = null);

    MigrationPlan CreateRecommendedPlan(
        InstanceInspectionResult source,
        InstanceInspectionResult destination,
        IReadOnlyDictionary<string, DestinationConflictDecision>? conflictDecisions = null);
}
