using System.ComponentModel;
using System.Runtime.CompilerServices;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.App.Presentation;

public sealed class MigrationSelectionEntryViewModel : INotifyPropertyChanged
{
    private static readonly IReadOnlyList<DestinationConflictDecision> Decisions =
        Array.AsReadOnly(Enum.GetValues<DestinationConflictDecision>());

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
            changed();
        }
    }

    public DestinationConflictDecision ConflictDecision
    {
        get => conflictDecision;
        set
        {
            if (conflictDecision == value ||
                (!CanChooseConflict && value != DestinationConflictDecision.Unresolved))
            {
                return;
            }

            conflictDecision = value;
            Notify();
            changed();
        }
    }

    public IReadOnlyList<DestinationConflictDecision> ConflictOptions => Decisions;

    public bool CanChooseConflict =>
        Selected &&
        PlanDisposition is
            MigrationPlanDisposition.DestinationConflict or
            MigrationPlanDisposition.SkippedDestinationConflict or
            MigrationPlanDisposition.ReadyToReplace;

    public object? SourceState { get; }

    public object? DestinationState { get; }

    public MigrationPreviewAction Action { get; }

    public MigrationPlanDisposition PlanDisposition { get; }

    public bool RequiresBackup { get; }

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
