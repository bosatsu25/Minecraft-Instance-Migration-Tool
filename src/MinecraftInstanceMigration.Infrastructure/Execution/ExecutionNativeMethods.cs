using System.Runtime.InteropServices;
using System.Text;
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
    private const uint VolumeNameGuid = 0x00000001;

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

    internal static string GetCanonicalVolumePath(SafeFileHandle handle)
    {
        uint capacity = 512;

        while (true)
        {
            var buffer = new StringBuilder(checked((int)capacity));
            uint length = GetFinalPathNameByHandleW(
                handle,
                buffer,
                capacity,
                VolumeNameGuid);

            if (length == 0)
            {
                throw new System.ComponentModel.Win32Exception(
                    Marshal.GetLastWin32Error());
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
        if (!GetDiskFreeSpaceExW(
                canonicalPath,
                out ulong available,
                out _,
                out _))
        {
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error());
        }

        return checked((long)available);
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
