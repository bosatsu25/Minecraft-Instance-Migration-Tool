using Microsoft.Win32.SafeHandles;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;
using MinecraftInstanceMigration.Domain.Rules;
using MinecraftInstanceMigration.Infrastructure.FileSystem;

namespace MinecraftInstanceMigration.Infrastructure.Execution;

// Merge (ADR 0004) composes the result from the validated backup of the destination and the source:
// for each name, the source unit wins and every other backup unit is kept. Writes are create-only into
// an emptied entry, so the same rule drives the read-only inspection, the rebuild, and the verifier.
// Names match case-insensitively, like the rest of the Windows path policy.
internal static class WindowsMergeTree
{
    internal static MergeInspectionResult Inspect(
        WindowsExecutionTree.OpenedNode source,
        WindowsExecutionTree.OpenedNode destination,
        string name,
        MergeUnit unit,
        CancellationToken cancellationToken)
    {
        var counts = new InspectionCounts();
        bool mergeable = unit == MergeUnit.File
            ? InspectFiles(source, destination, counts, cancellationToken)
            : InspectChildren(source, destination, counts, cancellationToken);

        return mergeable
            ? new MergeInspectionResult(
                name,
                unit,
                MergeInspectionStatus.Clean,
                counts.Added,
                counts.Replaced,
                counts.Kept,
                counts.BytesToWrite)
            : new MergeInspectionResult(name, unit, MergeInspectionStatus.KindConflict, 0, 0, 0, 0);
    }

    // Writes the merged entry into the destination root after the caller emptied it. The backup tree
    // stays held, so the kept content is exactly what the pre-state proof covered.
    internal static void Rebuild(
        WindowsExecutionTree.OpenedNode source,
        WindowsExecutionTree.HeldNodeTree backup,
        SafeFileHandle destinationRoot,
        string name,
        MergeUnit unit,
        CancellationToken cancellationToken)
    {
        using SafeFileHandle entry = WindowsExecutionTree.CreateOrOpenDirectory(destinationRoot, name);
        if (unit == MergeUnit.File)
        {
            RebuildFiles(source, backup, entry, cancellationToken);
        }
        else
        {
            RebuildChildren(source, backup, entry, cancellationToken);
        }
    }

    internal static bool MergedPayloadMatches(
        WindowsExecutionTree.OpenedNode source,
        WindowsExecutionTree.OpenedNode backup,
        WindowsExecutionTree.OpenedNode destination,
        MergeUnit unit,
        CancellationToken cancellationToken) =>
        unit == MergeUnit.File
            ? FilesMatch(source, backup, destination, cancellationToken)
            : ChildrenMatch(source, backup, destination, cancellationToken);

    private static bool InspectFiles(
        WindowsExecutionTree.OpenedNode source,
        WindowsExecutionTree.OpenedNode destination,
        InspectionCounts counts,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> destinationNames = Names(destination);
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string sourceName in NtFileApi.EnumerateNames(source.Handle))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using WindowsExecutionTree.OpenedNode sourceChild =
                WindowsExecutionTree.OpenExistingNode(source.Handle, sourceName);
            if (IsExcluded(sourceChild, sourceName))
            {
                continue;
            }

            if (!destinationNames.TryGetValue(sourceName, out string? destinationName))
            {
                counts.Added = checked(counts.Added + CountFiles(sourceChild, sourceName, cancellationToken));
                counts.AddBytes(WindowsExecutionTree.MeasureLogicalBytes(sourceChild, sourceName, cancellationToken));
                continue;
            }

            matched.Add(destinationName);
            using WindowsExecutionTree.OpenedNode destinationChild =
                WindowsExecutionTree.OpenExistingNode(destination.Handle, destinationName);
            if (sourceChild.IsDirectory != destinationChild.IsDirectory)
            {
                return false;
            }

            if (sourceChild.IsDirectory)
            {
                if (!InspectFiles(sourceChild, destinationChild, counts, cancellationToken))
                {
                    return false;
                }

                continue;
            }

            counts.Replaced = checked(counts.Replaced + 1);
            counts.AddBytes(WindowsExecutionTree.MeasureLogicalBytes(sourceChild, sourceName, cancellationToken));
        }

        CountKept(destination, destinationNames, matched, countChildrenAsUnits: false, counts, cancellationToken);
        return true;
    }

    private static bool InspectChildren(
        WindowsExecutionTree.OpenedNode source,
        WindowsExecutionTree.OpenedNode destination,
        InspectionCounts counts,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> destinationNames = Names(destination);
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string sourceName in NtFileApi.EnumerateNames(source.Handle))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using WindowsExecutionTree.OpenedNode sourceChild =
                WindowsExecutionTree.OpenExistingNode(source.Handle, sourceName);
            if (IsExcluded(sourceChild, sourceName))
            {
                continue;
            }

            counts.AddBytes(WindowsExecutionTree.MeasureLogicalBytes(sourceChild, sourceName, cancellationToken));
            if (!destinationNames.TryGetValue(sourceName, out string? destinationName))
            {
                counts.Added = checked(counts.Added + 1);
                continue;
            }

            matched.Add(destinationName);
            using WindowsExecutionTree.OpenedNode destinationChild =
                WindowsExecutionTree.OpenExistingNode(destination.Handle, destinationName);
            if (sourceChild.IsDirectory != destinationChild.IsDirectory)
            {
                return false;
            }

            // The replaced child is deleted; walking it still rejects a nested reparse point up front.
            WindowsExecutionTree.MeasureLogicalBytes(destinationChild, destinationName, cancellationToken);
            counts.Replaced = checked(counts.Replaced + 1);
        }

        CountKept(destination, destinationNames, matched, countChildrenAsUnits: true, counts, cancellationToken);
        return true;
    }

    private static void CountKept(
        WindowsExecutionTree.OpenedNode destination,
        Dictionary<string, string> destinationNames,
        HashSet<string> matched,
        bool countChildrenAsUnits,
        InspectionCounts counts,
        CancellationToken cancellationToken)
    {
        foreach (string destinationName in destinationNames.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (matched.Contains(destinationName))
            {
                continue;
            }

            using WindowsExecutionTree.OpenedNode child =
                WindowsExecutionTree.OpenExistingNode(destination.Handle, destinationName);
            if (IsExcluded(child, destinationName))
            {
                continue;
            }

            int units = countChildrenAsUnits ? 1 : CountFiles(child, destinationName, cancellationToken);
            counts.Kept = checked(counts.Kept + units);
            counts.AddBytes(WindowsExecutionTree.MeasureLogicalBytes(child, destinationName, cancellationToken));
        }
    }

    private static void RebuildFiles(
        WindowsExecutionTree.OpenedNode source,
        WindowsExecutionTree.HeldNodeTree backup,
        SafeFileHandle destination,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> sourceNames = SourceNames(source, cancellationToken);
        var descended = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (WindowsExecutionTree.HeldNodeTree backupChild in backup.Children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (WindowsExecutionTree.IsExcludedFile(backupChild))
            {
                continue;
            }

            if (!sourceNames.TryGetValue(backupChild.Name, out string? sourceName))
            {
                WindowsExecutionTree.CopyHeldTree(backupChild, destination, cancellationToken);
                continue;
            }

            using WindowsExecutionTree.OpenedNode sourceChild =
                WindowsExecutionTree.OpenExistingNode(source.Handle, sourceName);
            if (sourceChild.IsDirectory != backupChild.Node.IsDirectory)
            {
                throw new FileSystemException(FileSystemFailureKind.Changed);
            }

            if (sourceChild.IsDirectory)
            {
                // A directory present on both sides keeps the destination spelling.
                using SafeFileHandle child =
                    WindowsExecutionTree.CreateOrOpenDirectory(destination, backupChild.Name);
                RebuildFiles(sourceChild, backupChild, child, cancellationToken);
                descended.Add(sourceName);
            }
        }

        CopySourceChildren(source, sourceNames, descended, destination, cancellationToken);
    }

    private static void RebuildChildren(
        WindowsExecutionTree.OpenedNode source,
        WindowsExecutionTree.HeldNodeTree backup,
        SafeFileHandle destination,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> sourceNames = SourceNames(source, cancellationToken);

        foreach (WindowsExecutionTree.HeldNodeTree backupChild in backup.Children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (WindowsExecutionTree.IsExcludedFile(backupChild))
            {
                continue;
            }

            if (!sourceNames.TryGetValue(backupChild.Name, out string? sourceName))
            {
                WindowsExecutionTree.CopyHeldTree(backupChild, destination, cancellationToken);
                continue;
            }

            using WindowsExecutionTree.OpenedNode sourceChild =
                WindowsExecutionTree.OpenExistingNode(source.Handle, sourceName);
            if (sourceChild.IsDirectory != backupChild.Node.IsDirectory)
            {
                throw new FileSystemException(FileSystemFailureKind.Changed);
            }
        }

        CopySourceChildren(source, sourceNames, [], destination, cancellationToken);
    }

    private static void CopySourceChildren(
        WindowsExecutionTree.OpenedNode source,
        Dictionary<string, string> sourceNames,
        HashSet<string> alreadyMerged,
        SafeFileHandle destination,
        CancellationToken cancellationToken)
    {
        foreach (string sourceName in sourceNames.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (alreadyMerged.Contains(sourceName))
            {
                continue;
            }

            using WindowsExecutionTree.OpenedNode sourceChild =
                WindowsExecutionTree.OpenExistingNode(source.Handle, sourceName);

            // Directories may exist only because Replace-style deletion preserved excluded files in them.
            WindowsExecutionTree.CopyNode(
                sourceChild,
                destination,
                sourceName,
                cancellationToken,
                allowExistingDirectories: true);
        }
    }

    private static bool FilesMatch(
        WindowsExecutionTree.OpenedNode source,
        WindowsExecutionTree.OpenedNode backup,
        WindowsExecutionTree.OpenedNode destination,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> sourceNames = SourceNames(source, cancellationToken);
        Dictionary<string, string> destinationNames = Names(destination);
        var remaining = new HashSet<string>(destinationNames.Values, StringComparer.OrdinalIgnoreCase);
        var descended = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string backupName in NtFileApi.EnumerateNames(backup.Handle))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using WindowsExecutionTree.OpenedNode backupChild =
                WindowsExecutionTree.OpenExistingNode(backup.Handle, backupName);
            if (IsExcluded(backupChild, backupName))
            {
                continue;
            }

            bool shadowed = sourceNames.TryGetValue(backupName, out string? sourceName);
            if (shadowed && !backupChild.IsDirectory)
            {
                // The source file replaced it; the source pass checks the destination.
                continue;
            }

            if (!destinationNames.TryGetValue(backupName, out string? destinationName))
            {
                return false;
            }

            using WindowsExecutionTree.OpenedNode destinationChild =
                WindowsExecutionTree.OpenExistingNode(destination.Handle, destinationName);
            remaining.Remove(destinationName);

            if (!shadowed)
            {
                if (!WindowsExecutionTree.MigrationPayloadMatches(backupChild, destinationChild, cancellationToken))
                {
                    return false;
                }

                continue;
            }

            using WindowsExecutionTree.OpenedNode sourceChild =
                WindowsExecutionTree.OpenExistingNode(source.Handle, sourceName!);
            if (!sourceChild.IsDirectory ||
                !destinationChild.IsDirectory ||
                !FilesMatch(sourceChild, backupChild, destinationChild, cancellationToken))
            {
                return false;
            }

            descended.Add(sourceName!);
        }

        return SourceChildrenMatch(source, sourceNames, descended, destination, destinationNames, remaining, cancellationToken) &&
            OnlyExcludedRemain(destination, remaining, cancellationToken);
    }

    private static bool ChildrenMatch(
        WindowsExecutionTree.OpenedNode source,
        WindowsExecutionTree.OpenedNode backup,
        WindowsExecutionTree.OpenedNode destination,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> sourceNames = SourceNames(source, cancellationToken);
        Dictionary<string, string> destinationNames = Names(destination);
        var remaining = new HashSet<string>(destinationNames.Values, StringComparer.OrdinalIgnoreCase);

        foreach (string backupName in NtFileApi.EnumerateNames(backup.Handle))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using WindowsExecutionTree.OpenedNode backupChild =
                WindowsExecutionTree.OpenExistingNode(backup.Handle, backupName);
            if (IsExcluded(backupChild, backupName) || sourceNames.ContainsKey(backupName))
            {
                continue;
            }

            if (!destinationNames.TryGetValue(backupName, out string? destinationName))
            {
                return false;
            }

            using WindowsExecutionTree.OpenedNode destinationChild =
                WindowsExecutionTree.OpenExistingNode(destination.Handle, destinationName);
            remaining.Remove(destinationName);
            if (!WindowsExecutionTree.MigrationPayloadMatches(backupChild, destinationChild, cancellationToken))
            {
                return false;
            }
        }

        return SourceChildrenMatch(source, sourceNames, [], destination, destinationNames, remaining, cancellationToken) &&
            OnlyExcludedRemain(destination, remaining, cancellationToken);
    }

    private static bool SourceChildrenMatch(
        WindowsExecutionTree.OpenedNode source,
        Dictionary<string, string> sourceNames,
        HashSet<string> alreadyMatched,
        WindowsExecutionTree.OpenedNode destination,
        Dictionary<string, string> destinationNames,
        HashSet<string> remaining,
        CancellationToken cancellationToken)
    {
        foreach (string sourceName in sourceNames.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (alreadyMatched.Contains(sourceName))
            {
                continue;
            }

            using WindowsExecutionTree.OpenedNode sourceChild =
                WindowsExecutionTree.OpenExistingNode(source.Handle, sourceName);
            if (IsExcluded(sourceChild, sourceName))
            {
                continue;
            }

            if (!destinationNames.TryGetValue(sourceName, out string? destinationName))
            {
                return false;
            }

            using WindowsExecutionTree.OpenedNode destinationChild =
                WindowsExecutionTree.OpenExistingNode(destination.Handle, destinationName);
            remaining.Remove(destinationName);
            if (!WindowsExecutionTree.MigrationPayloadMatches(sourceChild, destinationChild, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private static bool OnlyExcludedRemain(
        WindowsExecutionTree.OpenedNode destination,
        HashSet<string> remaining,
        CancellationToken cancellationToken)
    {
        foreach (string name in remaining)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using WindowsExecutionTree.OpenedNode child =
                WindowsExecutionTree.OpenExistingNode(destination.Handle, name);
            if (!WindowsExecutionTree.ContainsOnlyExcludedContent(child, name, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private static int CountFiles(
        WindowsExecutionTree.OpenedNode node,
        string name,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!node.IsDirectory)
        {
            return KnownMigrationContentRules.IsExcludedFileName(name) ? 0 : 1;
        }

        int files = 0;
        foreach (string childName in NtFileApi.EnumerateNames(node.Handle))
        {
            using WindowsExecutionTree.OpenedNode child =
                WindowsExecutionTree.OpenExistingNode(node.Handle, childName);
            files = checked(files + CountFiles(child, childName, cancellationToken));
        }

        return files;
    }

    // An excluded source file is never written, so it neither shadows nor replaces a destination unit.
    private static Dictionary<string, string> SourceNames(
        WindowsExecutionTree.OpenedNode source,
        CancellationToken cancellationToken)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in NtFileApi.EnumerateNames(source.Handle))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using WindowsExecutionTree.OpenedNode child =
                WindowsExecutionTree.OpenExistingNode(source.Handle, name);
            if (!IsExcluded(child, name))
            {
                names.TryAdd(name, name);
            }
        }

        return names;
    }

    // Enumeration order is kept; the dictionary maps any spelling to the name on disk.
    private static Dictionary<string, string> Names(WindowsExecutionTree.OpenedNode directory)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in NtFileApi.EnumerateNames(directory.Handle))
        {
            names.TryAdd(name, name);
        }

        return names;
    }

    private static bool IsExcluded(WindowsExecutionTree.OpenedNode node, string name) =>
        !node.IsDirectory && KnownMigrationContentRules.IsExcludedFileName(name);

    private sealed class InspectionCounts
    {
        internal int Added { get; set; }

        internal int Replaced { get; set; }

        internal int Kept { get; set; }

        internal long BytesToWrite { get; private set; }

        internal void AddBytes(long bytes) => BytesToWrite = checked(BytesToWrite + bytes);
    }
}
