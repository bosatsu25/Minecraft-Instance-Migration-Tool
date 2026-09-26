using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MinecraftInstanceMigration.Infrastructure.Execution;

internal static class ExecutionNativeMethods
{
    internal const uint DeleteAccess = 0x00010000;
    internal const uint FileDeleteChild = 0x00000040;

    private const int FileDispositionInfoEx = 21;
    private const uint FileDispositionFlagDelete = 0x00000001;
    private const uint FileDispositionFlagPosixSemantics = 0x00000002;
    private const uint FileDispositionFlagIgnoreReadonlyAttribute = 0x00000010;

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
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfoExData
    {
        internal uint Flags;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle fileHandle,
        int fileInformationClass,
        ref FileDispositionInfoExData fileInformation,
        uint bufferSize);
}
