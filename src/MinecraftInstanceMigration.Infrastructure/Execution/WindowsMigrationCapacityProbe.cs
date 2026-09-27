using MinecraftInstanceMigration.Application.Capacity;
using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Infrastructure.Execution;

public sealed class WindowsMigrationCapacityProbe : ILogicalSizeProbe, IVolumeCapacityProbe
{
    public Task<LogicalSizeProbeResult> MeasureAsync(
        string root,
        string entryName,
        ExpectedEntryKind expectedKind,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => Measure(root, entryName, expectedKind, cancellationToken),
            cancellationToken);

    public Task<VolumeCapacityProbeResult> ProbeAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Probe(path, cancellationToken), cancellationToken);

    private static LogicalSizeProbeResult Measure(
        string root,
        string entryName,
        ExpectedEntryKind expectedKind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!WindowsExecutionTree.TryNormalizeRoot(root, out string normalized))
        {
            return new LogicalSizeProbeResult(LogicalSizeProbeStatus.Unavailable);
        }

        try
        {
            using WindowsExecutionTree.HeldDirectory heldRoot = WindowsExecutionTree.OpenDirectoryChain(normalized, writableFinal: false);
            using WindowsExecutionTree.OpenedNode node = WindowsExecutionTree.OpenExistingNode(heldRoot.Root, entryName);
            WindowsExecutionTree.EnsureExpectedKind(node, expectedKind);
            long bytes = WindowsExecutionTree.MeasureLogicalBytes(node, cancellationToken);
            return new LogicalSizeProbeResult(LogicalSizeProbeStatus.Available, bytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ExecutionTreeException error)
        {
            return new LogicalSizeProbeResult(MapSizeStatus(error.Kind));
        }
        catch (Exception)
        {
            return new LogicalSizeProbeResult(LogicalSizeProbeStatus.Unavailable);
        }
    }

    private static VolumeCapacityProbeResult Probe(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!WindowsExecutionTree.TryNormalizeRoot(path, out string normalized))
        {
            return new VolumeCapacityProbeResult(VolumeCapacityProbeStatus.InvalidPath);
        }

        try
        {
            using WindowsExecutionTree.HeldDirectory directory = WindowsExecutionTree.OpenDirectoryChain(normalized, writableFinal: false);
            string canonicalPath = ExecutionNativeMethods.GetCanonicalVolumePath(directory.Root);
            string volumeIdentity = GetVolumeIdentity(canonicalPath);
            long availableBytes = ExecutionNativeMethods.GetAvailableBytes(canonicalPath);
            return new VolumeCapacityProbeResult(
                VolumeCapacityProbeStatus.Available,
                volumeIdentity,
                availableBytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ExecutionTreeException error)
        {
            return new VolumeCapacityProbeResult(MapVolumeStatus(error.Kind));
        }
        catch (Exception)
        {
            return new VolumeCapacityProbeResult(VolumeCapacityProbeStatus.Unavailable);
        }
    }

    private static string GetVolumeIdentity(string canonicalPath)
    {
        const string prefix = @"\\?\Volume{";
        if (!canonicalPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Canonical volume identity was unavailable.");
        }

        int separator = canonicalPath.IndexOf('\\', prefix.Length);
        if (separator < 0)
        {
            throw new InvalidOperationException("Canonical volume identity was unavailable.");
        }

        return canonicalPath[..separator];
    }

    private static LogicalSizeProbeStatus MapSizeStatus(ExecutionTreeFailureKind kind) =>
        kind switch
        {
            ExecutionTreeFailureKind.Missing => LogicalSizeProbeStatus.Missing,
            ExecutionTreeFailureKind.ReparsePoint => LogicalSizeProbeStatus.ReparsePoint,
            ExecutionTreeFailureKind.AccessDenied => LogicalSizeProbeStatus.AccessDenied,
            _ => LogicalSizeProbeStatus.Unavailable,
        };

    private static VolumeCapacityProbeStatus MapVolumeStatus(ExecutionTreeFailureKind kind) =>
        kind switch
        {
            ExecutionTreeFailureKind.InvalidPath or ExecutionTreeFailureKind.Missing =>
                VolumeCapacityProbeStatus.InvalidPath,
            ExecutionTreeFailureKind.ReparsePoint => VolumeCapacityProbeStatus.ReparsePoint,
            ExecutionTreeFailureKind.AccessDenied => VolumeCapacityProbeStatus.AccessDenied,
            _ => VolumeCapacityProbeStatus.Unavailable,
        };
}
