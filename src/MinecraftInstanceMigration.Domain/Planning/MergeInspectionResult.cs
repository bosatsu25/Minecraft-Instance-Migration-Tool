using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Domain.Planning;

public enum MergeInspectionStatus
{
    Clean,
    KindConflict,
    ReparsePoint,
    Unavailable,
}

// Read-only, path-free evidence of what a Merge of one entry would do. Counts are in the
// candidate's merge unit: files for MergeUnit.File, top-level children for TopLevelChild.
public sealed record MergeInspectionResult
{
    public MergeInspectionResult(
        string name,
        MergeUnit unit,
        MergeInspectionStatus status,
        int unitsAdded,
        int unitsReplaced,
        int unitsKept,
        long bytesToWrite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (unit == MergeUnit.NotOffered || !Enum.IsDefined(unit))
        {
            throw new ArgumentOutOfRangeException(nameof(unit));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(unitsAdded);
        ArgumentOutOfRangeException.ThrowIfNegative(unitsReplaced);
        ArgumentOutOfRangeException.ThrowIfNegative(unitsKept);
        ArgumentOutOfRangeException.ThrowIfNegative(bytesToWrite);

        Name = name;
        Unit = unit;
        Status = status;
        UnitsAdded = unitsAdded;
        UnitsReplaced = unitsReplaced;
        UnitsKept = unitsKept;
        BytesToWrite = bytesToWrite;
    }

    public string Name { get; }

    public MergeUnit Unit { get; }

    public MergeInspectionStatus Status { get; }

    public int UnitsAdded { get; }

    public int UnitsReplaced { get; }

    public int UnitsKept { get; }

    public long BytesToWrite { get; }

    public bool IsClean => Status == MergeInspectionStatus.Clean;
}
