using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MinecraftInstanceMigration.Infrastructure.Backup;

internal static class BackupNativeMethods
{
    internal const uint FileReadData = 0x0001;
    internal const uint FileListDirectory = 0x0001;
    internal const uint FileWriteData = 0x0002;
    internal const uint FileAddFile = 0x0002;
    internal const uint FileAddSubdirectory = 0x0004;
    internal const uint FileTraverse = 0x0020;
    internal const uint FileReadAttributes = 0x0080;
    internal const uint FileWriteAttributes = 0x0100;
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

    internal const uint OpenExisting = 3;
    internal const uint BackupSemantics = 0x02000000;
    internal const uint OpenReparsePoint = 0x00200000;

    internal const int StatusReparsePointEncountered = unchecked((int)0xC000050B);
    internal const int StatusNoMoreFiles = unchecked((int)0x80000006);

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
                Attributes = 0x1040, // OBJ_CASE_INSENSITIVE | OBJ_DONT_REPARSE
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
                    12, // FileNamesInformation
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
                    throw new BackupNativeException(status);
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
        if (!GetFileInformationByHandleEx(
                handle,
                9,
                out AttributeTagInfo info,
                (uint)Marshal.SizeOf<AttributeTagInfo>()))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        return (FileAttributes)info.FileAttributes;
    }

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
    internal static extern uint RtlNtStatusToDosError(int status);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    internal static extern SafeFileHandle CreateFileW(
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
}

internal sealed class BackupNativeException(int status) : Exception
{
    internal int Status { get; } = status;
}
