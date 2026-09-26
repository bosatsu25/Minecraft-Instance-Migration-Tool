using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Domain.Execution;

public static class ExecutionLiveValidationPolicy
{
    public static ExecutionLiveValidationResult ValidateStep(
        MigrationPlan migrationPlan,
        ExecutionJournalEntry step,
        InstanceInspectionResult source,
        InstanceInspectionResult destination)
    {
        ArgumentNullException.ThrowIfNull(migrationPlan);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        var issues = new List<ExecutionLiveValidationIssue>();

        if (migrationPlan.Status != MigrationPlanStatus.Ready)
        {
            issues.Add(new ExecutionLiveValidationIssue(
                ExecutionLiveValidationIssueKind.MigrationPlanNotReady));
            return Invalid(issues);
        }

        MigrationPlanEntry? planned = migrationPlan.Entries.SingleOrDefault(entry =>
            string.Equals(entry.Name, step.Name, StringComparison.Ordinal));

        if (planned is null ||
            !planned.IsReadyForWrite ||
            planned.ExpectedKind != step.ExpectedKind ||
            planned.IsReadyToCopy != (step.Operation == ExecutionOperationKind.Copy) ||
            planned.IsReadyToReplace != (step.Operation == ExecutionOperationKind.Replace))
        {
            issues.Add(new ExecutionLiveValidationIssue(
                ExecutionLiveValidationIssueKind.JournalStepMismatch,
                step.Name));
            return Invalid(issues);
        }

        if (source.RootState != EntryState.Directory)
        {
            issues.Add(new ExecutionLiveValidationIssue(
                ExecutionLiveValidationIssueKind.SourceRootChanged,
                step.Name));
        }

        if (destination.RootState != EntryState.Directory)
        {
            issues.Add(new ExecutionLiveValidationIssue(
                ExecutionLiveValidationIssueKind.DestinationRootChanged,
                step.Name));
        }

        EntryObservation? sourceObservation = FindUniqueObservation(source, step.Name);
        EntryObservation? destinationObservation = FindUniqueObservation(destination, step.Name);

        if (sourceObservation is null ||
            sourceObservation.ExpectedKind != planned.ExpectedKind)
        {
            issues.Add(new ExecutionLiveValidationIssue(
                ExecutionLiveValidationIssueKind.SourceObservationInvalid,
                step.Name));
        }
        else if (sourceObservation.State != planned.SourceState)
        {
            issues.Add(new ExecutionLiveValidationIssue(
                ExecutionLiveValidationIssueKind.SourceStateChanged,
                step.Name));
        }

        if (destinationObservation is null ||
            destinationObservation.ExpectedKind != planned.ExpectedKind)
        {
            issues.Add(new ExecutionLiveValidationIssue(
                ExecutionLiveValidationIssueKind.DestinationObservationInvalid,
                step.Name));
        }
        else if (destinationObservation.State != planned.DestinationState)
        {
            issues.Add(new ExecutionLiveValidationIssue(
                ExecutionLiveValidationIssueKind.DestinationStateChanged,
                step.Name));
        }

        return issues.Count == 0
            ? new ExecutionLiveValidationResult(ExecutionLiveValidationStatus.Valid, [])
            : Invalid(issues);
    }

    private static ExecutionLiveValidationResult Invalid(
        IEnumerable<ExecutionLiveValidationIssue> issues) =>
        new(ExecutionLiveValidationStatus.Invalid, issues);

    private static EntryObservation? FindUniqueObservation(
        InstanceInspectionResult inspection,
        string name)
    {
        EntryObservation[] matches = inspection.Entries
            .Where(entry => string.Equals(entry.Name, name, StringComparison.Ordinal))
            .Take(2)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }
}
