using MinecraftInstanceMigration.Application.Planning;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;
using MinecraftInstanceMigration.Infrastructure.FileSystem;

namespace MinecraftInstanceMigration.Infrastructure.Execution;

// Read-only: opens both trees with the no-follow handle rules and reports what a Merge would do.
// Any reparse point blocks; any other failure to read both trees completely is Unavailable.
public sealed class WindowsMergeInspector : IMergeInspector
{
    public Task<MergeInspectionResult> InspectAsync(
        string sourceRoot,
        string destinationRoot,
        string entryName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryName);

        MergeUnit unit = KnownEntryCatalog.All
            .SingleOrDefault(candidate => string.Equals(candidate.Name, entryName, StringComparison.Ordinal))
            ?.MergeUnit ?? MergeUnit.NotOffered;
        if (unit == MergeUnit.NotOffered)
        {
            throw new ArgumentException("Merge is not offered for this entry.", nameof(entryName));
        }

        return Task.Run(
            () => Inspect(sourceRoot, destinationRoot, entryName, unit, cancellationToken),
            CancellationToken.None);
    }

    private static MergeInspectionResult Inspect(
        string sourceRoot,
        string destinationRoot,
        string name,
        MergeUnit unit,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Merge inspection requires Windows.");
        }

        if (!WindowsPathRules.TryNormalizeLocalDirectory(sourceRoot, out string normalizedSource) ||
            !WindowsPathRules.TryNormalizeLocalDirectory(destinationRoot, out string normalizedDestination) ||
            WindowsPathRules.RootsOverlap(normalizedSource, normalizedDestination))
        {
            return Result(name, unit, MergeInspectionStatus.Unavailable);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using WindowsExecutionTree.HeldDirectory source =
                WindowsExecutionTree.OpenDirectoryChain(normalizedSource, writableFinal: false);
            using WindowsExecutionTree.HeldDirectory destination =
                WindowsExecutionTree.OpenDirectoryChain(normalizedDestination, writableFinal: false);

            if (WindowsExecutionTree.PhysicalRootsOverlap(source, destination))
            {
                return Result(name, unit, MergeInspectionStatus.Unavailable);
            }

            using WindowsExecutionTree.OpenedNode sourceNode =
                WindowsExecutionTree.OpenExistingNode(source.Root, name);
            using WindowsExecutionTree.OpenedNode destinationNode =
                WindowsExecutionTree.OpenExistingNode(destination.Root, name);
            WindowsExecutionTree.EnsureExpectedKind(sourceNode, ExpectedEntryKind.Directory);
            WindowsExecutionTree.EnsureExpectedKind(destinationNode, ExpectedEntryKind.Directory);

            return WindowsMergeTree.Inspect(sourceNode, destinationNode, name, unit, cancellationToken);
        }
        catch (Exception error) when (
            error is not OperationCanceledException &&
            FileSystemErrors.TryClassify(error, out FileSystemFailureKind kind))
        {
            return Result(
                name,
                unit,
                kind == FileSystemFailureKind.ReparsePoint
                    ? MergeInspectionStatus.ReparsePoint
                    : MergeInspectionStatus.Unavailable);
        }
    }

    private static MergeInspectionResult Result(string name, MergeUnit unit, MergeInspectionStatus status) =>
        new(name, unit, status, 0, 0, 0, 0);
}
