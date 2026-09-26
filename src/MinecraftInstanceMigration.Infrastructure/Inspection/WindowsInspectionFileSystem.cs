using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using MinecraftInstanceMigration.Application.Inspection;
using MinecraftInstanceMigration.Domain.Inspection;

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

        if (!IsLocalPath(candidatePath))
        {
            return new Session(EntryState.InvalidPath, null, []);
        }

        string driveRoot = candidatePath[..3];
        if (new DriveInfo(driveRoot).DriveType == DriveType.Network)
        {
            return new Session(EntryState.InvalidPath, null, []);
        }

        List<SafeFileHandle> handles = [];
        try
        {
            string[] components = candidatePath[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
            for (int index = -1; index < components.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Resolve each single name relative to its held parent, never reopen ancestors by path.
                (SafeFileHandle handle, EntryState state) = index < 0
                    ? OpenDriveRoot(@"\\?\" + driveRoot)
                    : OpenChild(handles[^1], components[index], NativeMethods.ShareRead);
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

    private static bool IsLocalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length < 3 || path.Length > 32000 ||
            !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\')
        {
            return false;
        }

        return path[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries).All(IsSingleName);
    }

    private static bool IsSingleName(string name) =>
        name.Length > 0 && name is not "." and not ".." &&
        !name.EndsWith('.') && !name.EndsWith(' ') &&
        name.All(character => character >= 32 && !"<>:\"/\\|?*".Contains(character));

    private static (SafeFileHandle Handle, EntryState State) OpenDriveRoot(string path)
    {
        SafeFileHandle handle = NativeMethods.CreateFileW(
            path, NativeMethods.ReadAttributes, NativeMethods.ShareRead, IntPtr.Zero,
            NativeMethods.OpenExisting, NativeMethods.OpenReparsePoint | NativeMethods.BackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return (handle, MapError(Marshal.GetLastWin32Error()));
        }

        return (handle, ReadState(handle));
    }

    private static (SafeFileHandle Handle, EntryState State) OpenChild(SafeFileHandle parent, string name, uint share)
    {
        (SafeFileHandle handle, int status) = NativeMethods.OpenRelative(parent, name, share);
        if (status != 0)
        {
            return (handle, status == unchecked((int)0xC000050B) ? EntryState.ReparsePoint :
                MapError((int)NativeMethods.RtlNtStatusToDosError(status)));
        }

        return (handle, ReadState(handle));
    }

    private static EntryState ReadState(SafeFileHandle handle)
    {
        if (!NativeMethods.GetFileInformationByHandleEx(
                handle, 9, out NativeMethods.AttributeTagInfo info,
                (uint)Marshal.SizeOf<NativeMethods.AttributeTagInfo>()))
        {
            return MapError(Marshal.GetLastWin32Error());
        }

        FileAttributes attributes = (FileAttributes)info.FileAttributes;
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
            if (!IsSingleName(childName))
            {
                return EntryState.InvalidPath;
            }

            if (root is null)
            {
                throw new InvalidOperationException("The root is not an observable directory.");
            }

            (SafeFileHandle handle, EntryState state) = OpenChild(
                root, childName, NativeMethods.ShareRead | NativeMethods.ShareWrite | NativeMethods.ShareDelete);
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
