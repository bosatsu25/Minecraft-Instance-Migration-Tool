using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MinecraftInstanceMigration.Infrastructure.Inspection;

internal static class NativeMethods
{
    internal const uint ReadAttributes = 0x80;
    internal const uint ShareRead = 1;
    internal const uint ShareWrite = 2;
    internal const uint ShareDelete = 4;
    internal const uint OpenExisting = 3;
    internal const uint OpenReparsePoint = 0x00200000;
    internal const uint BackupSemantics = 0x02000000;

    internal static (SafeFileHandle Handle, int Status) OpenRelative(SafeFileHandle parent, string name, uint share)
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
            int status = NtCreateFile(out SafeFileHandle handle, ReadAttributes | 0x00100000,
                ref attributes, out _, IntPtr.Zero, 0, share, 1,
                OpenReparsePoint | 0x00400000 | 0x20, IntPtr.Zero, 0);
            // FILE_OPEN, FILE_OPEN_NO_RECALL, FILE_SYNCHRONOUS_IO_NONALERT.
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

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtCreateFile(out SafeFileHandle handle, uint desiredAccess,
        ref ObjectAttributes attributes, out IoStatusBlock ioStatus, IntPtr allocationSize,
        uint fileAttributes, uint shareAccess, uint disposition, uint options, IntPtr eaBuffer, uint eaLength);

    [DllImport("ntdll.dll", ExactSpelling = true)]
    internal static extern uint RtlNtStatusToDosError(int status);

    [StructLayout(LayoutKind.Sequential)]
    internal struct AttributeTagInfo
    {
        internal uint FileAttributes;
        internal uint ReparseTag;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    internal static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFileInformationByHandleEx(
        SafeFileHandle handle, int fileInformationClass, out AttributeTagInfo fileInformation, uint bufferSize);
}
