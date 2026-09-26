using Microsoft.Win32.SafeHandles;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Infrastructure.Backup;

namespace MinecraftInstanceMigration.Infrastructure.Execution;

public sealed class WindowsRollbackStorage : IRollbackStorage
{
    public Task<RollbackStorageResult> ApplyAsync(
        string destinationRoot,
        string? backupRoot,
        RollbackPlanEntry action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        return Task.Run(
            () => Apply(
                destinationRoot,
                backupRoot,
                action,
                cancellationToken),
            CancellationToken.None);
    }

    private static RollbackStorageResult Apply(
        string destinationRoot,
        string? backupRoot,
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
                action.Operation != ExecutionOperationKind.Replace)
        {
            return GuardRejected(RollbackStorageFailureKind.InvalidPlan);
        }

        if (!WindowsExecutionTree.TryNormalizeRoot(
                destinationRoot,
                out string destination))
        {
            return GuardRejected(RollbackStorageFailureKind.InvalidPath);
        }

        string? backup = null;
        if (action.Action == RollbackActionKind.RestoreFromBackup)
        {
            if (!WindowsExecutionTree.TryNormalizeRoot(
                    backupRoot,
                    out backup))
            {
                return GuardRejected(RollbackStorageFailureKind.InvalidPath);
            }

            if (WindowsExecutionTree.RootsOverlap(destination, backup))
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
                WindowsExecutionTree.OpenDirectoryChain(
                    backup!,
                    writableFinal: false);

            if (WindowsExecutionTree.PhysicalRootsOverlap(
                    destinationDirectory,
                    backupDirectory))
            {
                return GuardRejected(
                    RollbackStorageFailureKind.OverlappingRoots);
            }

            return RestoreFromBackup(
                destinationDirectory.Root,
                backupDirectory.Root,
                action,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return GuardRejected(RollbackStorageFailureKind.IoFailure);
        }
        catch (RollbackStorageException error)
        {
            return GuardRejected(error.Kind);
        }
        catch (ExecutionTreeException error)
        {
            return GuardRejected(MapGeneralFailure(error.Kind));
        }
        catch (UnauthorizedAccessException)
        {
            return GuardRejected(RollbackStorageFailureKind.AccessDenied);
        }
        catch (System.ComponentModel.Win32Exception error)
            when (error.NativeErrorCode == 5)
        {
            return GuardRejected(RollbackStorageFailureKind.AccessDenied);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return GuardRejected(RollbackStorageFailureKind.IoFailure);
        }
        catch (IOException)
        {
            return GuardRejected(RollbackStorageFailureKind.IoFailure);
        }
        catch (BackupNativeException error)
        {
            return GuardRejected(MapNativeFailure(error.Status));
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
            using (WindowsExecutionTree.OpenedNode destination =
                OpenDestinationForDelete(destinationRoot, action))
            {
                WindowsExecutionTree.TreeFingerprint actual =
                    WindowsExecutionTree.FingerprintOpenedNode(
                        destination,
                        action.Name,
                        cancellationToken);

                if (actual.ToDomain() != action.ExpectedCurrentFingerprint)
                {
                    return GuardRejected(
                        RollbackStorageFailureKind.DestinationChanged);
                }

                mutationStarted = true;
                WindowsExecutionTree.DeleteOpenedNode(
                    destination,
                    cancellationToken);
            }

            if (!IsMissing(destinationRoot, action.Name))
            {
                return RecoveryRequired(
                    RollbackStorageFailureKind.VerificationFailed);
            }

            return new RollbackStorageResult(
                RollbackStorageStatus.Applied);
        }
        catch (RollbackStorageException error)
        {
            return Failure(error.Kind, mutationStarted);
        }
        catch (ExecutionTreeException error)
        {
            return Failure(
                MapGeneralFailure(error.Kind),
                mutationStarted);
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(
                RollbackStorageFailureKind.AccessDenied,
                mutationStarted);
        }
        catch (System.ComponentModel.Win32Exception error)
            when (error.NativeErrorCode == 5)
        {
            return Failure(
                RollbackStorageFailureKind.AccessDenied,
                mutationStarted);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return Failure(
                RollbackStorageFailureKind.IoFailure,
                mutationStarted);
        }
        catch (IOException)
        {
            return Failure(
                RollbackStorageFailureKind.IoFailure,
                mutationStarted);
        }
        catch (BackupNativeException error)
        {
            return Failure(
                MapNativeFailure(error.Status),
                mutationStarted);
        }
    }

    private static RollbackStorageResult RestoreFromBackup(
        SafeFileHandle destinationRoot,
        SafeFileHandle backupRoot,
        RollbackPlanEntry action,
        CancellationToken cancellationToken)
    {
        bool mutationStarted = false;

        try
        {
            using WindowsExecutionTree.OpenedNode backup =
                OpenBackup(backupRoot, action);

            WindowsExecutionTree.TreeFingerprint backupBefore =
                WindowsExecutionTree.FingerprintOpenedNode(
                    backup,
                    action.Name,
                    cancellationToken);

            using (WindowsExecutionTree.OpenedNode destination =
                OpenDestinationForDelete(destinationRoot, action))
            {
                WindowsExecutionTree.TreeFingerprint current =
                    WindowsExecutionTree.FingerprintOpenedNode(
                        destination,
                        action.Name,
                        cancellationToken);

                if (current.ToDomain() != action.ExpectedCurrentFingerprint)
                {
                    return GuardRejected(
                        RollbackStorageFailureKind.DestinationChanged);
                }

                mutationStarted = true;
                WindowsExecutionTree.DeleteOpenedNode(
                    destination,
                    cancellationToken);
            }

            WindowsExecutionTree.CopyNode(
                backup,
                destinationRoot,
                action.Name,
                cancellationToken);

            WindowsExecutionTree.TreeFingerprint restored =
                WindowsExecutionTree.FingerprintNode(
                    destinationRoot,
                    action.Name,
                    action.ExpectedKind,
                    cancellationToken);

            WindowsExecutionTree.TreeFingerprint backupAfter =
                WindowsExecutionTree.FingerprintOpenedNode(
                    backup,
                    action.Name,
                    cancellationToken);

            if (backupBefore != backupAfter)
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
        catch (RollbackStorageException error)
        {
            return Failure(error.Kind, mutationStarted);
        }
        catch (ExecutionTreeException error)
        {
            return Failure(
                MapGeneralFailure(error.Kind),
                mutationStarted);
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(
                RollbackStorageFailureKind.AccessDenied,
                mutationStarted);
        }
        catch (System.ComponentModel.Win32Exception error)
            when (error.NativeErrorCode == 5)
        {
            return Failure(
                RollbackStorageFailureKind.AccessDenied,
                mutationStarted);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return Failure(
                RollbackStorageFailureKind.IoFailure,
                mutationStarted);
        }
        catch (IOException)
        {
            return Failure(
                RollbackStorageFailureKind.IoFailure,
                mutationStarted);
        }
        catch (BackupNativeException error)
        {
            return Failure(
                MapNativeFailure(error.Status),
                mutationStarted);
        }
    }

    private static WindowsExecutionTree.OpenedNode OpenDestinationForDelete(
        SafeFileHandle destinationRoot,
        RollbackPlanEntry action)
    {
        try
        {
            WindowsExecutionTree.OpenedNode node =
                WindowsExecutionTree.OpenExistingNode(
                    destinationRoot,
                    action.Name,
                    forDelete: true);

            try
            {
                WindowsExecutionTree.EnsureExpectedKind(
                    node,
                    action.ExpectedKind);
                return node;
            }
            catch
            {
                node.Dispose();
                throw;
            }
        }
        catch (ExecutionTreeException error) when (
            error.Kind == ExecutionTreeFailureKind.Missing)
        {
            throw new RollbackStorageException(
                RollbackStorageFailureKind.DestinationMissing);
        }
        catch (ExecutionTreeException error) when (
            error.Kind == ExecutionTreeFailureKind.Changed)
        {
            throw new RollbackStorageException(
                RollbackStorageFailureKind.DestinationChanged);
        }
    }

    private static WindowsExecutionTree.OpenedNode OpenBackup(
        SafeFileHandle backupRoot,
        RollbackPlanEntry action)
    {
        try
        {
            WindowsExecutionTree.OpenedNode node =
                WindowsExecutionTree.OpenExistingNode(
                    backupRoot,
                    action.Name);

            try
            {
                WindowsExecutionTree.EnsureExpectedKind(
                    node,
                    action.ExpectedKind);
                return node;
            }
            catch
            {
                node.Dispose();
                throw;
            }
        }
        catch (ExecutionTreeException error) when (
            error.Kind == ExecutionTreeFailureKind.Missing)
        {
            throw new RollbackStorageException(
                RollbackStorageFailureKind.BackupMissing);
        }
        catch (ExecutionTreeException error) when (
            error.Kind == ExecutionTreeFailureKind.Changed)
        {
            throw new RollbackStorageException(
                RollbackStorageFailureKind.BackupChanged);
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
        catch (ExecutionTreeException error)
            when (error.Kind == ExecutionTreeFailureKind.Missing)
        {
            return true;
        }
    }

    private static RollbackStorageFailureKind MapGeneralFailure(
        ExecutionTreeFailureKind kind) =>
        kind switch
        {
            ExecutionTreeFailureKind.InvalidPath =>
                RollbackStorageFailureKind.InvalidPath,
            ExecutionTreeFailureKind.OverlappingRoots =>
                RollbackStorageFailureKind.OverlappingRoots,
            ExecutionTreeFailureKind.Missing =>
                RollbackStorageFailureKind.DestinationMissing,
            ExecutionTreeFailureKind.Changed or
            ExecutionTreeFailureKind.Collision =>
                RollbackStorageFailureKind.DestinationChanged,
            ExecutionTreeFailureKind.ReparsePoint =>
                RollbackStorageFailureKind.ReparsePoint,
            ExecutionTreeFailureKind.AccessDenied =>
                RollbackStorageFailureKind.AccessDenied,
            _ => RollbackStorageFailureKind.IoFailure,
        };

    private static RollbackStorageFailureKind MapNativeFailure(
        int status)
    {
        if (status == BackupNativeMethods.StatusReparsePointEncountered)
        {
            return RollbackStorageFailureKind.ReparsePoint;
        }

        return BackupNativeMethods.RtlNtStatusToDosError(status) == 5
            ? RollbackStorageFailureKind.AccessDenied
            : RollbackStorageFailureKind.IoFailure;
    }

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
