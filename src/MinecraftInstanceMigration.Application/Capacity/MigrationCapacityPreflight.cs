using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Capacity;

public sealed class MigrationCapacityPreflight(
    ILogicalSizeProbe logicalSizeProbe,
    IVolumeCapacityProbe volumeCapacityProbe) : IMigrationCapacityPreflight
{
    private readonly ILogicalSizeProbe logicalSizeProbe =
        logicalSizeProbe ?? throw new ArgumentNullException(nameof(logicalSizeProbe));
    private readonly IVolumeCapacityProbe volumeCapacityProbe =
        volumeCapacityProbe ?? throw new ArgumentNullException(nameof(volumeCapacityProbe));

    public async Task<MigrationCapacityEstimate> EvaluateAsync(
        MigrationCapacityRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.SourceRoot) ||
            string.IsNullOrWhiteSpace(request.DestinationRoot) ||
            string.IsNullOrWhiteSpace(request.SafetyWorkspaceRoot))
        {
            return Failure(MigrationCapacityFailureKind.InvalidRequest);
        }

        if (request.Plan.Status != MigrationPlanStatus.Ready)
        {
            return Failure(MigrationCapacityFailureKind.PlanNotReady,
                MigrationCapacityStatus.Blocked);
        }

        try
        {
            long copyBytes = 0;
            long replaceWriteBytes = 0;
            long backupBytes = 0;

            foreach (MigrationPlanEntry entry in request.Plan.Entries.Where(entry => entry.IsReadyForWrite))
            {
                cancellationToken.ThrowIfCancellationRequested();
                LogicalSizeProbeResult source = await logicalSizeProbe.MeasureAsync(
                    request.SourceRoot,
                    entry.Name,
                    entry.ExpectedKind,
                    cancellationToken);
                if (!TryGetBytes(source, out long sourceBytes, out MigrationCapacityFailureKind failure))
                {
                    return Failure(failure);
                }

                if (entry.IsReadyToCopy)
                {
                    copyBytes = checked(copyBytes + sourceBytes);
                    continue;
                }

                replaceWriteBytes = checked(replaceWriteBytes + sourceBytes);
                LogicalSizeProbeResult destination = await logicalSizeProbe.MeasureAsync(
                    request.DestinationRoot,
                    entry.Name,
                    entry.ExpectedKind,
                    cancellationToken);
                if (!TryGetBytes(destination, out long destinationBytes, out failure))
                {
                    return Failure(failure);
                }

                backupBytes = checked(backupBytes + destinationBytes);
            }

            VolumeCapacityProbeResult destinationVolume = await volumeCapacityProbe.ProbeAsync(
                request.DestinationRoot,
                cancellationToken);
            VolumeCapacityProbeResult workspaceVolume = await volumeCapacityProbe.ProbeAsync(
                request.SafetyWorkspaceRoot,
                cancellationToken);
            if (!TryGetVolume(destinationVolume, out string destinationIdentity, out long destinationAvailable) ||
                !TryGetVolume(workspaceVolume, out string workspaceIdentity, out long workspaceAvailable))
            {
                return Failure(MigrationCapacityFailureKind.VolumeUnavailable);
            }

            long destinationLogical = checked(copyBytes + replaceWriteBytes);
            bool sharesVolume = string.Equals(
                destinationIdentity,
                workspaceIdentity,
                StringComparison.OrdinalIgnoreCase);

            if (sharesVolume)
            {
                long sharedLogical = checked(destinationLogical + backupBytes);
                long sharedRequired = MigrationCapacityMarginPolicy.RequiredBytes(sharedLogical);
                long sharedAvailable = Math.Min(destinationAvailable, workspaceAvailable);
                return new MigrationCapacityEstimate(
                    sharedAvailable >= sharedRequired
                        ? MigrationCapacityStatus.Ready
                        : MigrationCapacityStatus.InsufficientSharedVolumeSpace,
                    MigrationCapacityFailureKind.None,
                    copyBytes,
                    replaceWriteBytes,
                    backupBytes,
                    DestinationRequiredBytes: sharedRequired,
                    SafetyWorkspaceRequiredBytes: sharedRequired,
                    SharedVolumeRequiredBytes: sharedRequired,
                    DestinationAvailableBytes: destinationAvailable,
                    SafetyWorkspaceAvailableBytes: workspaceAvailable,
                    SharesVolume: true);
            }

            long destinationRequired = MigrationCapacityMarginPolicy.RequiredBytes(destinationLogical);
            long workspaceRequired = MigrationCapacityMarginPolicy.RequiredBytes(backupBytes);
            MigrationCapacityStatus status = destinationAvailable < destinationRequired
                ? MigrationCapacityStatus.InsufficientDestinationSpace
                : workspaceAvailable < workspaceRequired
                    ? MigrationCapacityStatus.InsufficientWorkspaceSpace
                    : MigrationCapacityStatus.Ready;

            return new MigrationCapacityEstimate(
                status,
                MigrationCapacityFailureKind.None,
                copyBytes,
                replaceWriteBytes,
                backupBytes,
                destinationRequired,
                workspaceRequired,
                DestinationAvailableBytes: destinationAvailable,
                SafetyWorkspaceAvailableBytes: workspaceAvailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(
                MigrationCapacityFailureKind.Cancelled,
                MigrationCapacityStatus.Cancelled);
        }
        catch (OverflowException)
        {
            return Failure(MigrationCapacityFailureKind.ArithmeticOverflow);
        }
        catch (Exception)
        {
            return Failure(MigrationCapacityFailureKind.MeasurementUnavailable);
        }
    }

    private static bool TryGetBytes(
        LogicalSizeProbeResult result,
        out long bytes,
        out MigrationCapacityFailureKind failure)
    {
        bytes = result.LogicalBytes ?? -1;
        failure = result.Status is LogicalSizeProbeStatus.ReparsePoint
            ? MigrationCapacityFailureKind.UnsafeTree
            : result.Status == LogicalSizeProbeStatus.Available
                ? MigrationCapacityFailureKind.InvalidMeasurement
                : MigrationCapacityFailureKind.MeasurementUnavailable;
        return result.Status == LogicalSizeProbeStatus.Available && bytes >= 0;
    }

    private static bool TryGetVolume(
        VolumeCapacityProbeResult result,
        out string identity,
        out long available)
    {
        identity = result.VolumeIdentity ?? "";
        available = result.AvailableBytes ?? -1;
        return result.Status == VolumeCapacityProbeStatus.Available &&
            !string.IsNullOrWhiteSpace(identity) &&
            available >= 0;
    }

    private static MigrationCapacityEstimate Failure(
        MigrationCapacityFailureKind failure,
        MigrationCapacityStatus status = MigrationCapacityStatus.Unavailable) =>
        new(status, failure);
}
