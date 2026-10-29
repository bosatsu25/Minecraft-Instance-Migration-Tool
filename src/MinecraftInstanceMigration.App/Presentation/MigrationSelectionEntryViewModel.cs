using System.ComponentModel;
using System.Runtime.CompilerServices;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.App.Presentation;

public sealed class MigrationSelectionEntryViewModel : INotifyPropertyChanged
{
    private readonly Action changed;
    private bool selected;
    private DestinationConflictDecision conflictDecision;

    public MigrationSelectionEntryViewModel(
        MigrationPreviewEntry entry,
        DestinationConflictDecision decision,
        Action changed)
    {
        ArgumentNullException.ThrowIfNull(entry);
        this.changed = changed ?? throw new ArgumentNullException(nameof(changed));

        Name = entry.Name;
        selected = entry.Selected;
        conflictDecision = decision;
        SourceState = entry.SourceState;
        DestinationState = entry.DestinationState;
        Action = entry.Action;
        PlanDisposition = entry.PlanDisposition;
        RequiresBackup = entry.RequiresBackup;
        MergeInspection = entry.MergeInspection;
        MergeUnit = KnownEntryCatalog.All
            .SingleOrDefault(candidate => string.Equals(candidate.Name, Name, StringComparison.Ordinal))
            ?.MergeUnit ?? MergeUnit.NotOffered;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public bool Selected
    {
        get => selected;
        set
        {
            if (selected == value)
            {
                return;
            }

            selected = value;
            if (!selected && conflictDecision != DestinationConflictDecision.Unresolved)
            {
                conflictDecision = DestinationConflictDecision.Unresolved;
                Notify(nameof(ConflictDecision));
            }

            Notify();
            Notify(nameof(CanChooseConflict));
            Notify(nameof(CanChooseMerge));
            changed();
        }
    }

    public DestinationConflictDecision ConflictDecision
    {
        get => conflictDecision;
        set
        {
            if (conflictDecision == value ||
                (!CanChooseConflict && value != DestinationConflictDecision.Unresolved) ||
                (value == DestinationConflictDecision.Merge && !CanChooseMerge))
            {
                return;
            }

            conflictDecision = value;
            Notify();
            changed();
        }
    }

    public bool CanChooseConflict =>
        Selected &&
        PlanDisposition is
            MigrationPlanDisposition.DestinationConflict or
            MigrationPlanDisposition.SkippedDestinationConflict or
            MigrationPlanDisposition.ReadyToReplace or
            MigrationPlanDisposition.BlockedDestinationKindMismatch or
            MigrationPlanDisposition.ReadyToMerge or
            MigrationPlanDisposition.MergeInspectionRequired or
            MigrationPlanDisposition.BlockedMergeConflict;

    // Merge is offered only where the catalog defines a merge unit (ADR 0004).
    public bool CanChooseMerge => CanChooseConflict && MergeUnit != MergeUnit.NotOffered;

    public MergeUnit MergeUnit { get; }

    public MergeInspectionResult? MergeInspection { get; }

    public EntryState? SourceState { get; }

    public EntryState? DestinationState { get; }

    public MigrationPreviewAction Action { get; }

    public MigrationPlanDisposition PlanDisposition { get; }

    public bool RequiresBackup { get; }

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
