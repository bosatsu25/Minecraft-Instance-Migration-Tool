using Microsoft.Win32.SafeHandles;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Infrastructure.Backup;
using MinecraftInstanceMigration.Infrastructure.FileSystem;

namespace MinecraftInstanceMigration.Infrastructure.Execution;

public sealed class WindowsRollbackStorage : IRollbackStorage
{
    public Task<RollbackStorageResult> ApplyAsync(
        string destinationRoot,
        string? backupRoot,
        RollbackBackupEvidence? backupEvidence,
        RollbackPlanEntry action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        return Task.Run(
            () => Apply(
                destinationRoot,
                backupRoot,
                backupEvidence,
                action,
                cancellationToken),
            CancellationToken.None);
    }

    private static RollbackStorageResult Apply(
        string destinationRoot,
        string? backupRoot,
        RollbackBackupEvidence? backupEvidence,
        RollbackPlanEntry action,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Rollback storage requires Windows.");
        }

        if (action.ExpectedCurrentFingerprint is null ||
            action.Action == RollbackActionKind.ManualRecoveryRequired ||
            action.Action == RollbackActionKind.DeleteCreatedEntry &&
                action.Operation != ExecutionOperationKind.Copy ||
            action.Action == RollbackActionKind.RestoreFromBackup &&
                action.Operation is not (ExecutionOperationKind.Replace or ExecutionOperationKind.Merge))
        {
            return GuardRejected(RollbackStorageFailureKind.InvalidPlan);
        }

        if (!WindowsPathRules.TryNormalizeLocalDirectory(
                destinationRoot,
                out string destination))
        {
            return GuardRejected(RollbackStorageFailureKind.InvalidPath);
        }

        string? backup = null;
        if (action.Action == RollbackActionKind.RestoreFromBackup)
        {
            if (backupEvidence is null ||
                !backupEvidence.Plan.CanStartBackup)
            {
                return GuardRejected(
                    RollbackStorageFailureKind.InvalidPlan);
            }

            if (!WindowsPathRules.TryNormalizeLocalDirectory(
                    backupRoot,
                    out backup))
            {
                return GuardRejected(RollbackStorageFailureKind.InvalidPath);
            }

            if (WindowsPathRules.RootsOverlap(destination, backup))
            {
                return GuardRejected(
                    RollbackStorageFailureKind.OverlappingRoots);
            }
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using WindowsExecutionTree.HeldDirectory destinationDirectory =
                WindowsExecutionTree.OpenDirectoryChain(
                    destination,
                    writableFinal: true);

            if (action.Action == RollbackActionKind.DeleteCreatedEntry)
            {
                return DeleteCreatedEntry(
                    destinationDirectory.Root,
                    action,
                    cancellationToken);
            }

            using WindowsExecutionTree.HeldDirectory backupDirectory =
                OpenBackupDirectory(backup!);

            if (WindowsExecutionTree.PhysicalRootsOverlap(
                    destinationDirectory,
                    backupDirectory))
            {
                return GuardRejected(
                    RollbackStorageFailureKind.OverlappingRoots);
            }

            var backupTrees = new List<WindowsExecutionTree.HeldNodeTree>();
            try
            {
                WindowsExecutionTree.HeldNodeTree? actionBackup = null;
                foreach (var entry in backupEvidence!.Plan.Entries)
                {
                    WindowsExecutionTree.HeldNodeTree tree =
                        OpenBackupTree(backupDirectory.Root, entry.Name);
                    backupTrees.Add(tree);
                    WindowsExecutionTree.EnsureExpectedKind(
                        tree.Node, entry.ExpectedKind);
                    if (entry.Name == action.Name &&
                        entry.ExpectedKind == action.ExpectedKind)
                    {
                        actionBackup = tree;
                    }
                }

                if (actionBackup is null)
                {
                    return GuardRejected(
                        RollbackStorageFailureKind.InvalidPlan);
                }

                // Pin metadata as well as payload while a second complete validation runs.
                // The first validation's summary alone cannot prove that the manifest and
                // ownership marker survived the gap before these handles were acquired.
                using WindowsExecutionTree.HeldNodeTree owner =
                    OpenBackupTree(
                        backupDirectory.Root, ".mim-backup-owner.json");
                using WindowsExecutionTree.HeldNodeTree manifest =
                    OpenBackupTree(
                        backupDirectory.Root, "backup-manifest.json");
                if (owner.Node.IsDirectory || manifest.Node.IsDirectory)
                {
                    return GuardRejected(
                        RollbackStorageFailureKind.BackupChanged);
                }

                var revalidation = new WindowsBackupStorage()
                    .ValidateBackupAsync(
                        backup!, backupEvidence.Plan, cancellationToken)
                    .GetAwaiter().GetResult();
                if (!revalidation.IsValid ||
                    revalidation.Verification is null ||
                    revalidation.Verification != backupEvidence.Verification)
                {
                    return GuardRejected(
                        RollbackStorageFailureKind.BackupChanged);
                }

                WindowsExecutionTree.TreeFingerprint validatedBackup =
                    WindowsExecutionTree.FingerprintHeldBackupPlan(
                        backupTrees, cancellationToken);

                if (!MatchesBackupEvidence(
                        validatedBackup,
                        backupEvidence.Verification))
                {
                    return GuardRejected(
                        RollbackStorageFailureKind.BackupChanged);
                }

                return RestoreFromBackup(
                    destinationDirectory.Root,
                    backupTrees,
                    actionBackup,
                    validatedBackup,
                    action,
                    cancellationToken);
            }
            finally
            {
                foreach (WindowsExecutionTree.HeldNodeTree tree in backupTrees)
                {
                    tree.Dispose();
                }
            }
        }
        catch (OperationCanceledException)
        {
            return GuardRejected(RollbackStorageFailureKind.IoFailure);
        }
        catch (RollbackStorageException error)
        {
            return GuardRejected(error.Kind);
        }
        catch (Exception error) when (FileSystemErrors.TryClassify(error, out FileSystemFailureKind kind))
        {
            return GuardRejected(MapGeneralFailure(kind));
        }
    }

    private static RollbackStorageResult DeleteCreatedEntry(
        SafeFileHandle destinationRoot,
        RollbackPlanEntry action,
        CancellationToken cancellationToken)
    {
        bool mutationStarted = false;

        try
        {
            using (WindowsExecutionTree.HeldNodeTree destination =
                OpenDestinationTree(destinationRoot, action))
            {
                WindowsExecutionTree.TreeFingerprint actual =
                    WindowsExecutionTree.FingerprintHeldTree(
                        destination, cancellationToken);

                if (actual.ToDomain() != action.ExpectedCurrentFingerprint)
                {
                    return GuardRejected(
                        RollbackStorageFailureKind.DestinationChanged);
                }

                WindowsExecutionTree.DeleteHeldTree(
                    destination, () => mutationStarted = true);
            }

            if (!IsMissing(destinationRoot, action.Name))
            {
                return RecoveryRequired(
                    RollbackStorageFailureKind.VerificationFailed);
            }

            return new RollbackStorageResult(
                RollbackStorageStatus.Applied);
        }
        catch (OperationCanceledException)
        {
            // Cancellation after the guarded delete began cannot be reported as an untouched destination.
            return Failure(RollbackStorageFailureKind.IoFailure, mutationStarted);
        }
        catch (RollbackStorageException error)
        {
            return Failure(error.Kind, mutationStarted);
        }
        catch (Exception error) when (FileSystemErrors.TryClassify(error, out FileSystemFailureKind kind))
        {
            return Failure(
                MapGeneralFailure(kind),
                mutationStarted);
        }
    }

    private static RollbackStorageResult RestoreFromBackup(
        SafeFileHandle destinationRoot,
        IReadOnlyList<WindowsExecutionTree.HeldNodeTree> backupTrees,
        WindowsExecutionTree.HeldNodeTree backup,
        WindowsExecutionTree.TreeFingerprint validatedBackup,
        RollbackPlanEntry action,
        CancellationToken cancellationToken)
    {
        bool mutationStarted = false;

        try
        {
            WindowsExecutionTree.TreeFingerprint backupBefore =
                WindowsExecutionTree.FingerprintHeldTree(
                    backup, cancellationToken);

            using (WindowsExecutionTree.HeldNodeTree destination =
                OpenDestinationTree(destinationRoot, action))
            {
                WindowsExecutionTree.TreeFingerprint current =
                    WindowsExecutionTree.FingerprintHeldTree(
                        destination, cancellationToken);

                if (current.ToDomain() != action.ExpectedCurrentFingerprint)
                {
                    return GuardRejected(
                        RollbackStorageFailureKind.DestinationChanged);
                }

                WindowsExecutionTree.DeleteHeldTree(
                    destination, () => mutationStarted = true);
            }

            WindowsExecutionTree.CopyHeldTree(
                backup,
                destinationRoot,
                cancellationToken);

            WindowsExecutionTree.TreeFingerprint restored =
                WindowsExecutionTree.FingerprintNode(
                    destinationRoot,
                    action.Name,
                    action.ExpectedKind,
                    cancellationToken);

            WindowsExecutionTree.TreeFingerprint backupAfter =
                WindowsExecutionTree.FingerprintHeldTree(
                    backup, cancellationToken);

            WindowsExecutionTree.TreeFingerprint aggregateAfter =
                WindowsExecutionTree.FingerprintHeldBackupPlan(
                    backupTrees, cancellationToken);

            if (backupBefore != backupAfter ||
                validatedBackup != aggregateAfter)
            {
                return RecoveryRequired(
                    RollbackStorageFailureKind.BackupChanged);
            }

            if (restored != backupAfter)
            {
                return RecoveryRequired(
                    RollbackStorageFailureKind.VerificationFailed);
            }

            return new RollbackStorageResult(
                RollbackStorageStatus.Applied);
        }
        catch (OperationCanceledException)
        {
            // Cancellation after the guarded delete began cannot be reported as an untouched destination.
            return Failure(RollbackStorageFailureKind.IoFailure, mutationStarted);
        }
        catch (RollbackStorageException error)
        {
            return Failure(error.Kind, mutationStarted);
        }
        catch (Exception error) when (FileSystemErrors.TryClassify(error, out FileSystemFailureKind kind))
        {
            return Failure(
                MapGeneralFailure(kind),
                mutationStarted);
        }
    }

    private static WindowsExecutionTree.HeldNodeTree OpenDestinationTree(
        SafeFileHandle destinationRoot,
        RollbackPlanEntry action)
    {
        try
        {
            WindowsExecutionTree.HeldNodeTree tree =
                WindowsExecutionTree.OpenHeldTree(
                    destinationRoot,
                    action.Name,
                    forDelete: true);

            try
            {
                WindowsExecutionTree.EnsureExpectedKind(
                    tree.Node,
                    action.ExpectedKind);
                return tree;
            }
            catch
            {
                tree.Dispose();
                throw;
            }
        }
        catch (FileSystemException error) when (
            error.Kind == FileSystemFailureKind.Missing)
        {
            throw new RollbackStorageException(
                RollbackStorageFailureKind.DestinationMissing);
        }
        catch (FileSystemException error) when (
            error.Kind == FileSystemFailureKind.Changed)
        {
            throw new RollbackStorageException(
                RollbackStorageFailureKind.DestinationChanged);
        }
    }

    private static WindowsExecutionTree.HeldDirectory OpenBackupDirectory(
        string path)
    {
        try
        {
            return WindowsExecutionTree.OpenDirectoryChain(
                path, writableFinal: false);
        }
        catch (FileSystemException error) when (
            error.Kind == FileSystemFailureKind.Missing)
        {
            throw new RollbackStorageException(
                RollbackStorageFailureKind.BackupMissing);
        }
    }

    private static WindowsExecutionTree.HeldNodeTree OpenBackupTree(
        SafeFileHandle root,
        string name)
    {
        try
        {
            return WindowsExecutionTree.OpenHeldTree(
                root, name, forDelete: false);
        }
        catch (FileSystemException error) when (
            error.Kind == FileSystemFailureKind.Missing)
        {
            throw new RollbackStorageException(
                RollbackStorageFailureKind.BackupMissing);
        }
    }

    private static bool IsMissing(
        SafeFileHandle parent,
        string name)
    {
        try
        {
            using WindowsExecutionTree.OpenedNode _ =
                WindowsExecutionTree.OpenExistingNode(parent, name);
            return false;
        }
        catch (FileSystemException error)
            when (error.Kind == FileSystemFailureKind.Missing)
        {
            return true;
        }
    }

    private static bool MatchesBackupEvidence(
        WindowsExecutionTree.TreeFingerprint actual,
        MinecraftInstanceMigration.Application.Backup.BackupVerificationSummary expected) =>
        actual.FileCount == expected.FileCount &&
        actual.DirectoryCount == expected.DirectoryCount &&
        actual.TotalBytes == expected.TotalBytes &&
        string.Equals(
            actual.Sha256,
            expected.Sha256,
            StringComparison.OrdinalIgnoreCase);

    private static RollbackStorageFailureKind MapGeneralFailure(
        FileSystemFailureKind kind) =>
        kind switch
        {
            FileSystemFailureKind.InvalidPath =>
                RollbackStorageFailureKind.InvalidPath,
            FileSystemFailureKind.OverlappingRoots =>
                RollbackStorageFailureKind.OverlappingRoots,
            FileSystemFailureKind.Missing =>
                RollbackStorageFailureKind.DestinationMissing,
            FileSystemFailureKind.Changed or
            FileSystemFailureKind.Collision =>
                RollbackStorageFailureKind.DestinationChanged,
            FileSystemFailureKind.ReparsePoint =>
                RollbackStorageFailureKind.ReparsePoint,
            FileSystemFailureKind.AccessDenied =>
                RollbackStorageFailureKind.AccessDenied,
            _ => RollbackStorageFailureKind.IoFailure,
        };


    private static RollbackStorageResult Failure(
        RollbackStorageFailureKind kind,
        bool mutationStarted) =>
        mutationStarted
            ? RecoveryRequired(kind)
            : GuardRejected(kind);

    private static RollbackStorageResult GuardRejected(
        RollbackStorageFailureKind kind) =>
        new(
            RollbackStorageStatus.GuardRejected,
            kind);

    private static RollbackStorageResult RecoveryRequired(
        RollbackStorageFailureKind kind) =>
        new(
            RollbackStorageStatus.RecoveryRequired,
            kind);

    private sealed class RollbackStorageException(
        RollbackStorageFailureKind kind) : Exception
    {
        internal RollbackStorageFailureKind Kind { get; } = kind;
    }
}
