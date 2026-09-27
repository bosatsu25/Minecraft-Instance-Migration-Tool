using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Capacity;

public enum LogicalSizeProbeStatus
{
    Available,
    Missing,
    ReparsePoint,
    AccessDenied,
    Unavailable,
}

public sealed record LogicalSizeProbeResult(
    LogicalSizeProbeStatus Status,
    long? LogicalBytes = null);

public enum VolumeCapacityProbeStatus
{
    Available,
    InvalidPath,
    ReparsePoint,
    AccessDenied,
    Unavailable,
}

public sealed record VolumeCapacityProbeResult(
    VolumeCapacityProbeStatus Status,
    string? VolumeIdentity = null,
    long? AvailableBytes = null);

public enum MigrationCapacityStatus
{
    Ready,
    InsufficientDestinationSpace,
    InsufficientWorkspaceSpace,
    InsufficientSharedVolumeSpace,
    Unavailable,
    Blocked,
    Cancelled,
}

public enum MigrationCapacityFailureKind
{
    None,
    InvalidRequest,
    PlanNotReady,
    MeasurementUnavailable,
    UnsafeTree,
    VolumeUnavailable,
    InvalidMeasurement,
    ArithmeticOverflow,
    Cancelled,
}

public sealed record MigrationCapacityRequest(
    string SourceRoot,
    string DestinationRoot,
    string SafetyWorkspaceRoot,
    MigrationPlan Plan);

public sealed record MigrationCapacityEstimate(
    MigrationCapacityStatus Status,
    MigrationCapacityFailureKind FailureKind,
    long CopyBytes = 0,
    long ReplaceWriteBytes = 0,
    long BackupBytes = 0,
    long DestinationRequiredBytes = 0,
    long SafetyWorkspaceRequiredBytes = 0,
    long? SharedVolumeRequiredBytes = null,
    long? DestinationAvailableBytes = null,
    long? SafetyWorkspaceAvailableBytes = null,
    bool SharesVolume = false)
{
    public bool IsReady => Status == MigrationCapacityStatus.Ready;
}

public interface ILogicalSizeProbe
{
    Task<LogicalSizeProbeResult> MeasureAsync(
        string root,
        string entryName,
        ExpectedEntryKind expectedKind,
        CancellationToken cancellationToken = default);
}

public interface IVolumeCapacityProbe
{
    Task<VolumeCapacityProbeResult> ProbeAsync(
        string path,
        CancellationToken cancellationToken = default);
}

public interface IMigrationCapacityPreflight
{
    Task<MigrationCapacityEstimate> EvaluateAsync(
        MigrationCapacityRequest request,
        CancellationToken cancellationToken = default);
}
