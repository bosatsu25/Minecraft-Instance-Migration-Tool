using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Infrastructure.Execution;
using MinecraftInstanceMigration.Infrastructure.FileSystem;

namespace MinecraftInstanceMigration.Infrastructure.Backup;

public sealed class WindowsBackupStorage : IBackupStorage, IBackupArtifactValidationStorage
{
    private const string OwnerMarkerName = ".mim-backup-owner.json";
    private const string ManifestName = "backup-manifest.json";

    // The backup parent only needs to receive one new owned directory.
    private const uint BackupParentAccess =
        NtFileApi.FileAddFile | NtFileApi.FileAddSubdirectory | NtFileApi.FileWriteAttributes;

    // Metadata must match exactly what this adapter writes; anything else is not an owned artifact.
    private static readonly JsonSerializerOptions StrictReadOptions = new()
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectRequiredConstructorParameters = true,
    };

    private static readonly JsonSerializerOptions ManifestWriteOptions = new() { WriteIndented = true };

    public Task<BackupExecutionResult> CreateBackupAsync(
        string destinationRoot,
        string backupParent,
        BackupPlan plan,
        BackupManifestDraft manifest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(manifest);

        return Task.Run(
            () => CreateBackup(destinationRoot, backupParent, plan, manifest, cancellationToken),
            CancellationToken.None);
    }

    public Task<BackupArtifactValidationResult> ValidateBackupAsync(
        string backupRoot,
        BackupPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return Task.Run(
            () => ValidateBackup(backupRoot, plan, cancellationToken),
            CancellationToken.None);
    }

    private static BackupArtifactValidationResult ValidateBackup(
        string backupRoot,
        BackupPlan plan,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Backup validation requires Windows.");
        }

        if (!plan.CanStartBackup)
        {
            return InvalidArtifact(BackupArtifactFailureKind.InvalidPlan);
        }

        if (!WindowsPathRules.TryNormalizeLocalDirectory(backupRoot, out string normalizedBackupRoot))
        {
            return InvalidArtifact(BackupArtifactFailureKind.InvalidPath);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using WindowsExecutionTree.HeldDirectory root =
                OpenRoot(normalizedBackupRoot, WindowsExecutionTree.ReadOnlyDirectoryAccess);

            OwnerMarker owner = ReadJsonFile<OwnerMarker>(
                root.Root,
                OwnerMarkerName,
                4096,
                BackupArtifactFailureKind.OwnershipMarkerInvalid,
                cancellationToken);

            if (owner.FormatVersion != 1 ||
                !Guid.TryParseExact(owner.ExecutionId, "N", out _) ||
                !string.Equals(
                    Path.GetFileName(normalizedBackupRoot),
                    "mim-backup-" + owner.ExecutionId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new BackupArtifactValidationException(BackupArtifactFailureKind.OwnershipMarkerInvalid);
            }

            CompletedManifest manifest = ReadJsonFile<CompletedManifest>(
                root.Root,
                ManifestName,
                64 * 1024,
                BackupArtifactFailureKind.ManifestInvalid,
                cancellationToken);

            if (!CompletedManifestMatchesPlan(plan, manifest))
            {
                throw new BackupArtifactValidationException(BackupArtifactFailureKind.ManifestInvalid);
            }

            IReadOnlyList<string> actualNames = NtFileApi.EnumerateNames(root.Root);
            var expectedNames = new HashSet<string>(
                plan.Entries.Select(entry => entry.Name),
                StringComparer.OrdinalIgnoreCase)
            {
                OwnerMarkerName,
                ManifestName,
            };

            if (actualNames.Count != expectedNames.Count ||
                actualNames.Any(name => !expectedNames.Contains(name)))
            {
                throw new BackupArtifactValidationException(BackupArtifactFailureKind.UnexpectedContent);
            }

            WindowsExecutionTree.TreeFingerprint actual =
                WindowsExecutionTree.FingerprintBackupEntries(root.Root, plan, cancellationToken);
            if (!FingerprintMatches(actual, manifest.Verification))
            {
                throw new BackupArtifactValidationException(BackupArtifactFailureKind.VerificationMismatch);
            }

            return new BackupArtifactValidationResult(
                BackupArtifactValidationStatus.Valid,
                Verification: ToSummary(actual));
        }
        catch (OperationCanceledException)
        {
            return new BackupArtifactValidationResult(BackupArtifactValidationStatus.Cancelled);
        }
        catch (BackupArtifactValidationException error)
        {
            return InvalidArtifact(error.Kind);
        }
        catch (BackupStorageException error)
        {
            return InvalidArtifact(MapValidationFailure(error.Kind));
        }
        catch (Exception error) when (FileSystemErrors.TryClassify(error, out FileSystemFailureKind kind))
        {
            return InvalidArtifact(MapValidationFailure(MapPayloadFailure(kind)));
        }
    }

    private static BackupExecutionResult CreateBackup(
        string destinationRoot,
        string backupParent,
        BackupPlan plan,
        BackupManifestDraft manifest,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Backup storage requires Windows.");
        }

        if (!ManifestMatchesPlan(plan, manifest))
        {
            return new BackupExecutionResult(
                BackupExecutionStatus.Failed,
                FailureKind: BackupFailureKind.InvalidPlan);
        }

        if (!WindowsPathRules.TryNormalizeLocalDirectory(destinationRoot, out string normalizedDestination) ||
            !WindowsPathRules.TryNormalizeLocalDirectory(backupParent, out string normalizedBackupParent))
        {
            return new BackupExecutionResult(
                BackupExecutionStatus.Failed,
                FailureKind: BackupFailureKind.InvalidPath);
        }

        if (WindowsPathRules.IsEqualOrDescendant(normalizedBackupParent, normalizedDestination))
        {
            return new BackupExecutionResult(
                BackupExecutionStatus.Failed,
                FailureKind: BackupFailureKind.OverlappingRoots);
        }

        string? backupRootPath = null;
        int entriesCopied = 0;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using WindowsExecutionTree.HeldDirectory destination =
                OpenRoot(normalizedDestination, WindowsExecutionTree.ReadOnlyDirectoryAccess);
            using WindowsExecutionTree.HeldDirectory backupParentDirectory =
                OpenRoot(normalizedBackupParent, BackupParentAccess);

            // Drive aliases can differ lexically while naming a destination descendant.
            if (WindowsExecutionTree.IsPhysicalEqualOrDescendant(backupParentDirectory.Root, destination.Root))
            {
                return new BackupExecutionResult(
                    BackupExecutionStatus.Failed,
                    FailureKind: BackupFailureKind.OverlappingRoots);
            }

            string executionId = Guid.NewGuid().ToString("N");
            string backupRootName = $"mim-backup-{executionId}";
            backupRootPath = Path.Combine(normalizedBackupParent, backupRootName);

            if (WindowsPathRules.IsEqualOrDescendant(backupRootPath, normalizedDestination))
            {
                return new BackupExecutionResult(
                    BackupExecutionStatus.Failed,
                    FailureKind: BackupFailureKind.OverlappingRoots);
            }

            using SafeFileHandle backupRoot =
                WindowsExecutionTree.CreateDirectory(backupParentDirectory.Root, backupRootName);

            // The ownership marker comes first so a partial root is still recognisably ours.
            WriteOwnedFile(
                backupRoot,
                OwnerMarkerName,
                JsonSerializer.SerializeToUtf8Bytes(new OwnerMarker(1, executionId)),
                cancellationToken);

            foreach (BackupPlanEntry entry in plan.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using WindowsExecutionTree.OpenedNode source =
                    WindowsExecutionTree.OpenExistingNode(destination.Root, entry.Name);
                if (source.IsDirectory != (entry.DestinationState == EntryState.Directory))
                {
                    throw new BackupStorageException(BackupFailureKind.SourceChanged);
                }

                WindowsExecutionTree.CopyNode(source, backupRoot, entry.Name, cancellationToken);
                entriesCopied++;
            }

            // Independent verification: source, backup, then source again.
            WindowsExecutionTree.TreeFingerprint sourceFirst =
                WindowsExecutionTree.FingerprintBackupEntries(destination.Root, plan, cancellationToken);
            WindowsExecutionTree.TreeFingerprint backup =
                WindowsExecutionTree.FingerprintBackupEntries(backupRoot, plan, cancellationToken);
            WindowsExecutionTree.TreeFingerprint sourceSecond =
                WindowsExecutionTree.FingerprintBackupEntries(destination.Root, plan, cancellationToken);

            if (!sourceFirst.Equals(sourceSecond))
            {
                throw new BackupStorageException(BackupFailureKind.SourceChanged);
            }

            if (!sourceSecond.Equals(backup))
            {
                throw new BackupStorageException(BackupFailureKind.VerificationFailed);
            }

            BackupVerificationSummary verification = ToSummary(backup);
            byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(
                new CompletedManifest(
                    manifest.SchemaVersion,
                    manifest.Entries.Select(entry => new ManifestEntry(
                        entry.Name,
                        entry.ExpectedKind.ToString(),
                        entry.DestinationState.ToString())).ToArray(),
                    verification),
                ManifestWriteOptions);

            // Only a verified copy receives the completion manifest.
            WriteOwnedFile(backupRoot, ManifestName, manifestBytes, cancellationToken);

            return new BackupExecutionResult(
                BackupExecutionStatus.Completed,
                backupRootPath,
                EntriesCopied: entriesCopied,
                Verification: verification);
        }
        catch (OperationCanceledException)
        {
            return new BackupExecutionResult(
                BackupExecutionStatus.Cancelled,
                backupRootPath,
                EntriesCopied: entriesCopied);
        }
        catch (BackupStorageException error)
        {
            return new BackupExecutionResult(
                BackupExecutionStatus.Failed,
                backupRootPath,
                error.Kind,
                entriesCopied);
        }
        catch (Exception error) when (FileSystemErrors.TryClassify(error, out FileSystemFailureKind kind))
        {
            return new BackupExecutionResult(
                BackupExecutionStatus.Failed,
                backupRootPath,
                MapPayloadFailure(kind),
                entriesCopied);
        }
    }

    // An absent or non-directory root is an invalid path rather than payload drift.
    private static WindowsExecutionTree.HeldDirectory OpenRoot(string path, uint finalAccess)
    {
        try
        {
            return WindowsExecutionTree.OpenDirectoryChain(path, finalAccess);
        }
        catch (FileSystemException error)
        {
            throw new BackupStorageException(error.Kind switch
            {
                FileSystemFailureKind.Missing or FileSystemFailureKind.InvalidPath => BackupFailureKind.InvalidPath,
                FileSystemFailureKind.ReparsePoint => BackupFailureKind.ReparsePoint,
                FileSystemFailureKind.AccessDenied => BackupFailureKind.AccessDenied,
                _ => BackupFailureKind.IoFailure,
            });
        }
    }

    private static T ReadJsonFile<T>(
        SafeFileHandle parent,
        string name,
        int maximumBytes,
        BackupArtifactFailureKind invalidKind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WindowsExecutionTree.OpenedNode node;
        try
        {
            node = WindowsExecutionTree.OpenExistingNode(parent, name);
        }
        catch (FileSystemException error) when (
            error.Kind is FileSystemFailureKind.Missing or FileSystemFailureKind.Changed)
        {
            throw new BackupArtifactValidationException(invalidKind);
        }

        using (node)
        {
            if (node.IsDirectory)
            {
                throw new BackupArtifactValidationException(invalidKind);
            }

            long length = RandomAccess.GetLength(node.Handle);
            if (length <= 0 || length > maximumBytes)
            {
                throw new BackupArtifactValidationException(invalidKind);
            }

            byte[] bytes = new byte[checked((int)length)];
            int offset = 0;
            while (offset < bytes.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = RandomAccess.Read(node.Handle, bytes.AsSpan(offset), offset);
                if (read == 0)
                {
                    throw new BackupArtifactValidationException(invalidKind);
                }

                offset += read;
            }

            try
            {
                T? value = JsonSerializer.Deserialize<T>(bytes, StrictReadOptions);
                return value ?? throw new BackupArtifactValidationException(invalidKind);
            }
            catch (JsonException)
            {
                throw new BackupArtifactValidationException(invalidKind);
            }
        }
    }

    private static void WriteOwnedFile(
        SafeFileHandle parent,
        string name,
        byte[] content,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SafeFileHandle file = WindowsExecutionTree.CreateFile(parent, name);
        if (content.Length > 0)
        {
            RandomAccess.Write(file, content, 0);
        }

        RandomAccess.FlushToDisk(file);
    }

    private static bool CompletedManifestMatchesPlan(BackupPlan plan, CompletedManifest manifest)
    {
        if (manifest.SchemaVersion != BackupManifestDraft.CurrentSchemaVersion ||
            manifest.Entries is null ||
            manifest.Verification is null ||
            plan.Entries.Count != manifest.Entries.Count ||
            manifest.Verification.FileCount < 0 ||
            manifest.Verification.DirectoryCount < 0 ||
            manifest.Verification.TotalBytes < 0 ||
            !ChecksummedJournal.IsSha256Hex(manifest.Verification.Sha256))
        {
            return false;
        }

        for (int index = 0; index < plan.Entries.Count; index++)
        {
            BackupPlanEntry planEntry = plan.Entries[index];
            ManifestEntry manifestEntry = manifest.Entries[index];
            if (manifestEntry is null ||
                !string.Equals(planEntry.Name, manifestEntry.Name, StringComparison.Ordinal) ||
                !string.Equals(planEntry.ExpectedKind.ToString(), manifestEntry.ExpectedKind, StringComparison.Ordinal) ||
                !string.Equals(planEntry.DestinationState.ToString(), manifestEntry.DestinationState, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ManifestMatchesPlan(BackupPlan plan, BackupManifestDraft manifest)
    {
        if (!plan.CanStartBackup ||
            manifest.SchemaVersion != BackupManifestDraft.CurrentSchemaVersion ||
            plan.Entries.Count != manifest.Entries.Count)
        {
            return false;
        }

        for (int index = 0; index < plan.Entries.Count; index++)
        {
            BackupPlanEntry planEntry = plan.Entries[index];
            BackupManifestEntryDraft manifestEntry = manifest.Entries[index];
            if (!string.Equals(planEntry.Name, manifestEntry.Name, StringComparison.Ordinal) ||
                planEntry.ExpectedKind != manifestEntry.ExpectedKind ||
                planEntry.DestinationState != manifestEntry.DestinationState)
            {
                return false;
            }
        }

        return true;
    }

    private static bool FingerprintMatches(
        WindowsExecutionTree.TreeFingerprint actual,
        BackupVerificationSummary expected) =>
        actual.FileCount == expected.FileCount &&
        actual.DirectoryCount == expected.DirectoryCount &&
        actual.TotalBytes == expected.TotalBytes &&
        string.Equals(actual.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase);

    private static BackupVerificationSummary ToSummary(WindowsExecutionTree.TreeFingerprint fingerprint) =>
        new(fingerprint.FileCount, fingerprint.DirectoryCount, fingerprint.TotalBytes, fingerprint.Sha256);

    // Payload that disappears or changes kind while it is being copied is source drift.
    private static BackupFailureKind MapPayloadFailure(FileSystemFailureKind kind) =>
        kind switch
        {
            FileSystemFailureKind.Missing or FileSystemFailureKind.Changed => BackupFailureKind.SourceChanged,
            FileSystemFailureKind.InvalidPath => BackupFailureKind.InvalidPath,
            FileSystemFailureKind.OverlappingRoots => BackupFailureKind.OverlappingRoots,
            FileSystemFailureKind.ReparsePoint => BackupFailureKind.ReparsePoint,
            FileSystemFailureKind.AccessDenied => BackupFailureKind.AccessDenied,
            _ => BackupFailureKind.IoFailure,
        };

    private static BackupArtifactFailureKind MapValidationFailure(BackupFailureKind kind) =>
        kind switch
        {
            BackupFailureKind.InvalidPlan => BackupArtifactFailureKind.InvalidPlan,
            BackupFailureKind.InvalidPath => BackupArtifactFailureKind.InvalidPath,
            BackupFailureKind.ReparsePoint => BackupArtifactFailureKind.ReparsePoint,
            BackupFailureKind.AccessDenied => BackupArtifactFailureKind.AccessDenied,
            BackupFailureKind.SourceChanged or BackupFailureKind.VerificationFailed =>
                BackupArtifactFailureKind.VerificationMismatch,
            _ => BackupArtifactFailureKind.IoFailure,
        };

    private static BackupArtifactValidationResult InvalidArtifact(BackupArtifactFailureKind kind) =>
        new(BackupArtifactValidationStatus.Invalid, kind);

    private sealed class BackupStorageException(BackupFailureKind kind) : Exception
    {
        public BackupFailureKind Kind { get; } = kind;
    }

    private sealed class BackupArtifactValidationException(BackupArtifactFailureKind kind) : Exception
    {
        public BackupArtifactFailureKind Kind { get; } = kind;
    }

    private sealed record OwnerMarker(int FormatVersion, string ExecutionId);

    private sealed record ManifestEntry(string Name, string ExpectedKind, string DestinationState);

    private sealed record CompletedManifest(
        int SchemaVersion,
        IReadOnlyList<ManifestEntry> Entries,
        BackupVerificationSummary Verification);
}
