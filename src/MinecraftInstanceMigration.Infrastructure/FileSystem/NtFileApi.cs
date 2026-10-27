using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MinecraftInstanceMigration.Infrastructure.FileSystem;

// The only Windows interop surface in Infrastructure. Every child is opened as one name relative to
// a held parent handle with OBJ_DONT_REPARSE, so no adapter resolves user paths by string.
internal static class NtFileApi
{
    internal const uint FileReadData = 0x0001;
    internal const uint FileListDirectory = 0x0001;
    internal const uint FileWriteData = 0x0002;
    internal const uint FileAddFile = 0x0002;
    internal const uint FileAddSubdirectory = 0x0004;
    internal const uint FileTraverse = 0x0020;
    internal const uint FileDeleteChild = 0x0040;
    internal const uint FileReadAttributes = 0x0080;
    internal const uint FileWriteAttributes = 0x0100;
    internal const uint DeleteAccess = 0x00010000;
    internal const uint Synchronize = 0x00100000;

    internal const uint ShareRead = 0x00000001;
    internal const uint ShareWrite = 0x00000002;
    internal const uint ShareDelete = 0x00000004;

    internal const uint FileOpen = 1;
    internal const uint FileCreate = 2;

    internal const uint FileDirectoryFile = 0x00000001;
    internal const uint FileSynchronousIoNonAlert = 0x00000020;
    internal const uint FileNonDirectoryFile = 0x00000040;
    internal const uint FileOpenReparsePoint = 0x00200000;
    internal const uint FileOpenNoRecall = 0x00400000;

    // Options for opening an existing object without following or recalling it.
    internal const uint NoFollowOpenOptions =
        FileOpenReparsePoint | FileOpenNoRecall | FileSynchronousIoNonAlert;

    internal const int StatusReparsePointEncountered = unchecked((int)0xC000050B);

    private const int StatusNoMoreFiles = unchecked((int)0x80000006);
    private const uint ObjCaseInsensitive = 0x00000040;
    private const uint ObjDontReparse = 0x00001000;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;
    private const uint OpenReparsePointFlag = 0x00200000;
    private const int FileAttributeTagInfo = 9;
    private const int FileNamesInformation = 12;
    private const int FileDispositionInfoEx = 21;
    private const uint FileDispositionFlagDelete = 0x00000001;
    private const uint FileDispositionFlagPosixSemantics = 0x00000002;
    private const uint FileDispositionFlagIgnoreReadonlyAttribute = 0x00000010;
    private const uint VolumeNameGuid = 0x00000001;

    internal static (SafeFileHandle Handle, int Status) OpenRelative(
        SafeFileHandle parent,
        string name,
        uint desiredAccess,
        uint shareAccess,
        uint disposition,
        uint options)
    {
        IntPtr text = Marshal.StringToHGlobalUni(name);
        IntPtr unicodePointer = IntPtr.Zero;
        bool retained = false;
        try
        {
            var unicode = new UnicodeString
            {
                Length = checked((ushort)(name.Length * 2)),
                MaximumLength = checked((ushort)(name.Length * 2 + 2)),
                Buffer = text,
            };
            unicodePointer = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
            Marshal.StructureToPtr(unicode, unicodePointer, false);
            parent.DangerousAddRef(ref retained);
            var attributes = new ObjectAttributes
            {
                Length = Marshal.SizeOf<ObjectAttributes>(),
                RootDirectory = parent.DangerousGetHandle(),
                ObjectName = unicodePointer,
                Attributes = ObjCaseInsensitive | ObjDontReparse,
            };
            int status = NtCreateFile(
                out SafeFileHandle handle,
                desiredAccess,
                ref attributes,
                out _,
                IntPtr.Zero,
                0,
                shareAccess,
                disposition,
                options,
                IntPtr.Zero,
                0);
            return (handle, status);
        }
        finally
        {
            if (retained)
            {
                parent.DangerousRelease();
            }

            Marshal.FreeHGlobal(unicodePointer);
            Marshal.FreeHGlobal(text);
        }
    }

    // Opens a drive root without following a reparse point. Returns the Win32 error on failure.
    internal static (SafeFileHandle Handle, int Error) OpenDriveRoot(string driveRoot, uint desiredAccess)
    {
        SafeFileHandle handle = CreateFileW(
            @"\\?\" + driveRoot,
            desiredAccess,
            ShareRead,
            IntPtr.Zero,
            OpenExisting,
            BackupSemantics | OpenReparsePointFlag,
            IntPtr.Zero);
        return (handle, handle.IsInvalid ? Marshal.GetLastWin32Error() : 0);
    }

    internal static IReadOnlyList<string> EnumerateNames(SafeFileHandle directory)
    {
        const int bufferSize = 64 * 1024;
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            var names = new List<string>();
            bool restart = true;
            while (true)
            {
                int status = NtQueryDirectoryFile(
                    directory,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    out IoStatusBlock ioStatus,
                    buffer,
                    bufferSize,
                    FileNamesInformation,
                    false,
                    IntPtr.Zero,
                    restart);
                restart = false;

                if (status == StatusNoMoreFiles)
                {
                    break;
                }

                if (status != 0)
                {
                    throw new FileSystemException(FileSystemErrors.ForEnumeration(status));
                }

                int available = checked((int)ioStatus.Information.ToUInt64());
                int offset = 0;
                while (offset < available)
                {
                    uint nextOffset = unchecked((uint)Marshal.ReadInt32(buffer, offset));
                    uint nameLength = unchecked((uint)Marshal.ReadInt32(buffer, offset + 8));
                    string name = Marshal.PtrToStringUni(
                        IntPtr.Add(buffer, offset + 12),
                        checked((int)nameLength / 2)) ?? "";

                    if (name is not "." and not "..")
                    {
                        names.Add(name);
                    }

                    if (nextOffset == 0)
                    {
                        break;
                    }

                    offset = checked(offset + (int)nextOffset);
                }
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static FileAttributes ReadAttributes(SafeFileHandle handle)
    {
        int error = TryReadAttributes(handle, out FileAttributes attributes);
        return error == 0 ? attributes : throw new Win32Exception(error);
    }

    // Returns 0 on success, otherwise the Win32 error, so read-only observers can map it without throwing.
    internal static int TryReadAttributes(SafeFileHandle handle, out FileAttributes attributes)
    {
        if (!GetFileInformationByHandleEx(
                handle,
                FileAttributeTagInfo,
                out AttributeTagInfo info,
                (uint)Marshal.SizeOf<AttributeTagInfo>()))
        {
            attributes = default;
            return Marshal.GetLastWin32Error();
        }

        attributes = (FileAttributes)info.FileAttributes;
        return 0;
    }

    internal static void DeleteByHandle(SafeFileHandle handle)
    {
        var info = new FileDispositionInfoExData
        {
            Flags = FileDispositionFlagDelete |
                FileDispositionFlagPosixSemantics |
                FileDispositionFlagIgnoreReadonlyAttribute,
        };

        if (!SetFileInformationByHandle(
                handle,
                FileDispositionInfoEx,
                ref info,
                (uint)Marshal.SizeOf<FileDispositionInfoExData>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    internal static string GetCanonicalVolumePath(SafeFileHandle handle)
    {
        uint capacity = 512;

        while (true)
        {
            var buffer = new StringBuilder(checked((int)capacity));
            uint length = GetFinalPathNameByHandleW(handle, buffer, capacity, VolumeNameGuid);

            if (length == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (length < capacity)
            {
                return buffer.ToString();
            }

            capacity = checked(length + 1);
        }
    }

    internal static long GetAvailableBytes(string canonicalPath)
    {
        if (!GetDiskFreeSpaceExW(canonicalPath, out ulong available, out _, out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return checked((long)available);
    }

    internal static uint ToDosError(int status) => RtlNtStatusToDosError(status);

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        internal ushort Length;
        internal ushort MaximumLength;
        internal IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        internal int Length;
        internal IntPtr RootDirectory;
        internal IntPtr ObjectName;
        internal uint Attributes;
        internal IntPtr SecurityDescriptor;
        internal IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock
    {
        internal IntPtr Status;
        internal UIntPtr Information;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTagInfo
    {
        internal uint FileAttributes;
        internal uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfoExData
    {
        internal uint Flags;
    }

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtCreateFile(
        out SafeFileHandle handle,
        uint desiredAccess,
        ref ObjectAttributes attributes,
        out IoStatusBlock ioStatus,
        IntPtr allocationSize,
        uint fileAttributes,
        uint shareAccess,
        uint disposition,
        uint options,
        IntPtr eaBuffer,
        uint eaLength);

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtQueryDirectoryFile(
        SafeFileHandle fileHandle,
        IntPtr eventHandle,
        IntPtr apcRoutine,
        IntPtr apcContext,
        out IoStatusBlock ioStatusBlock,
        IntPtr fileInformation,
        int length,
        int fileInformationClass,
        [MarshalAs(UnmanagedType.U1)] bool returnSingleEntry,
        IntPtr fileName,
        [MarshalAs(UnmanagedType.U1)] bool restartScan);

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern uint RtlNtStatusToDosError(int status);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle handle,
        int fileInformationClass,
        out AttributeTagInfo fileInformation,
        uint bufferSize);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle fileHandle,
        int fileInformationClass,
        ref FileDispositionInfoExData fileInformation,
        uint bufferSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle fileHandle,
        StringBuilder filePath,
        uint filePathLength,
        uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceExW(
        string directoryName,
        out ulong freeBytesAvailableToCaller,
        out ulong totalNumberOfBytes,
        out ulong totalNumberOfFreeBytes);
}
