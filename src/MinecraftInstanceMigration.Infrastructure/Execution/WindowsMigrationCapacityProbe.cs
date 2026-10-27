using MinecraftInstanceMigration.Application.Capacity;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Infrastructure.FileSystem;

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
        if (!WindowsPathRules.TryNormalizeLocalDirectory(root, out string normalized))
        {
            return new LogicalSizeProbeResult(LogicalSizeProbeStatus.Unavailable);
        }

        try
        {
            using WindowsExecutionTree.HeldDirectory heldRoot = WindowsExecutionTree.OpenDirectoryChain(normalized, writableFinal: false);
            using WindowsExecutionTree.OpenedNode node = WindowsExecutionTree.OpenExistingNode(heldRoot.Root, entryName);
            WindowsExecutionTree.EnsureExpectedKind(node, expectedKind);
            long bytes = WindowsExecutionTree.MeasureLogicalBytes(
                node,
                entryName,
                cancellationToken);
            return new LogicalSizeProbeResult(LogicalSizeProbeStatus.Available, bytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FileSystemException error)
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
        if (!WindowsPathRules.TryNormalizeLocalDirectory(path, out string normalized))
        {
            return new VolumeCapacityProbeResult(VolumeCapacityProbeStatus.InvalidPath);
        }

        try
        {
            using WindowsExecutionTree.HeldDirectory directory = WindowsExecutionTree.OpenDirectoryChain(normalized, writableFinal: false);
            string canonicalPath = NtFileApi.GetCanonicalVolumePath(directory.Root);
            string volumeIdentity = GetVolumeIdentity(canonicalPath);
            long availableBytes = NtFileApi.GetAvailableBytes(canonicalPath);
            return new VolumeCapacityProbeResult(
                VolumeCapacityProbeStatus.Available,
                volumeIdentity,
                availableBytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FileSystemException error)
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

    private static LogicalSizeProbeStatus MapSizeStatus(FileSystemFailureKind kind) =>
        kind switch
        {
            FileSystemFailureKind.Missing => LogicalSizeProbeStatus.Missing,
            FileSystemFailureKind.ReparsePoint => LogicalSizeProbeStatus.ReparsePoint,
            FileSystemFailureKind.AccessDenied => LogicalSizeProbeStatus.AccessDenied,
            _ => LogicalSizeProbeStatus.Unavailable,
        };

    private static VolumeCapacityProbeStatus MapVolumeStatus(FileSystemFailureKind kind) =>
        kind switch
        {
            FileSystemFailureKind.InvalidPath or FileSystemFailureKind.Missing =>
                VolumeCapacityProbeStatus.InvalidPath,
            FileSystemFailureKind.ReparsePoint => VolumeCapacityProbeStatus.ReparsePoint,
            FileSystemFailureKind.AccessDenied => VolumeCapacityProbeStatus.AccessDenied,
            _ => VolumeCapacityProbeStatus.Unavailable,
        };
}
