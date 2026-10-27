using Microsoft.Win32.SafeHandles;
using MinecraftInstanceMigration.Application.Inspection;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Infrastructure.FileSystem;

namespace MinecraftInstanceMigration.Infrastructure.Inspection;

public sealed class WindowsInspectionFileSystem : IInspectionFileSystem
{
    public IInspectionSession OpenRoot(string candidatePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Inspection requires Windows.");
        }

        // The Inspector may observe a drive root; write adapters separately reject it.
        if (!WindowsPathRules.TryNormalizeLocal(candidatePath, allowDriveRoot: true, out string normalized))
        {
            return new Session(EntryState.InvalidPath, null, []);
        }

        string driveRoot = normalized[..3];
        List<SafeFileHandle> handles = [];
        try
        {
            string[] components = normalized[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
            for (int index = -1; index < components.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Resolve each single name relative to its held parent, never reopen ancestors by path.
                (SafeFileHandle handle, EntryState state) = index < 0
                    ? OpenDriveRoot(driveRoot)
                    : OpenChild(handles[^1], components[index], NtFileApi.ShareRead);
                handles.Add(handle);
                cancellationToken.ThrowIfCancellationRequested();
                if (state != EntryState.Directory)
                {
                    if (state == EntryState.File && index < components.Length - 1)
                    {
                        state = EntryState.Missing;
                    }

                    foreach (SafeFileHandle held in handles)
                    {
                        held.Dispose();
                    }

                    return new Session(state, null, []);
                }
            }

            return new Session(EntryState.Directory, handles[^1], handles);
        }
        catch
        {
            foreach (SafeFileHandle handle in handles)
            {
                handle.Dispose();
            }

            throw;
        }
    }

    private static (SafeFileHandle Handle, EntryState State) OpenDriveRoot(string driveRoot)
    {
        (SafeFileHandle handle, int error) = NtFileApi.OpenDriveRoot(driveRoot, NtFileApi.FileReadAttributes);
        if (handle.IsInvalid)
        {
            return (handle, MapError(error));
        }

        return (handle, ReadState(handle));
    }

    // Attribute-only access: no data, listing, write, or delete rights are ever requested.
    private static (SafeFileHandle Handle, EntryState State) OpenChild(SafeFileHandle parent, string name, uint share)
    {
        (SafeFileHandle handle, int status) = NtFileApi.OpenRelative(
            parent,
            name,
            NtFileApi.FileReadAttributes | NtFileApi.Synchronize,
            share,
            NtFileApi.FileOpen,
            NtFileApi.NoFollowOpenOptions);
        if (status != 0)
        {
            return (handle, status == NtFileApi.StatusReparsePointEncountered ? EntryState.ReparsePoint :
                MapError((int)NtFileApi.ToDosError(status)));
        }

        return (handle, ReadState(handle));
    }

    private static EntryState ReadState(SafeFileHandle handle)
    {
        int error = NtFileApi.TryReadAttributes(handle, out FileAttributes attributes);
        if (error != 0)
        {
            return MapError(error);
        }

        return attributes.HasFlag(FileAttributes.ReparsePoint) ? EntryState.ReparsePoint :
            attributes.HasFlag(FileAttributes.Directory) ? EntryState.Directory : EntryState.File;
    }

    private static EntryState MapError(int error) => error switch
    {
        2 or 3 => EntryState.Missing,
        5 => EntryState.Inaccessible,
        123 or 161 or 206 => EntryState.InvalidPath,
        _ => EntryState.Unavailable,
    };

    private sealed class Session(EntryState rootState, SafeFileHandle? root, List<SafeFileHandle> handles)
        : IInspectionSession
    {
        private bool disposed;
        public EntryState RootState { get; } = rootState;

        public EntryState ObserveChild(string childName, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (!WindowsPathRules.IsSingleName(childName))
            {
                return EntryState.InvalidPath;
            }

            if (root is null)
            {
                throw new InvalidOperationException("The root is not an observable directory.");
            }

            (SafeFileHandle handle, EntryState state) = OpenChild(
                root, childName, NtFileApi.ShareRead | NtFileApi.ShareWrite | NtFileApi.ShareDelete);
            using (handle)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return state;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            foreach (SafeFileHandle handle in handles)
            {
                handle.Dispose();
            }
        }
    }
}
