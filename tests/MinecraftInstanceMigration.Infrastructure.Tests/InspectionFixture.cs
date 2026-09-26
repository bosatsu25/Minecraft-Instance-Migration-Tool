using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MinecraftInstanceMigration.Infrastructure.Tests;

internal sealed class InspectionFixture : IDisposable
{
    private readonly List<string> _junctions = [];
    public string Root { get; } = Directory.CreateTempSubdirectory("mim-inspector-").FullName;
    public string At(string relative) => Path.GetFullPath(Path.Combine(Root, relative));

    public void Junction(string relative, string targetRelative)
    {
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "/c", "mklink", "/J", At(relative), At(targetRelative) })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        if (!process.WaitForExit(10000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Assert.Fail("Fixture junction creation timed out.");
        }
        Assert.Equal(0, process.ExitCode);
        _junctions.Add(At(relative));
        Assert.True(File.GetAttributes(At(relative)).HasFlag(FileAttributes.ReparsePoint));
    }

    public string[] Snapshot()
    {
        return Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories)
            .Prepend(Root)
            .Order(StringComparer.Ordinal)
            .Select(path =>
            {
                var attributes = File.GetAttributes(path);
                var content = attributes.HasFlag(FileAttributes.Directory)
                    ? "" : Convert.ToHexString(File.ReadAllBytes(path));
                return $"{Path.GetRelativePath(Root, path)}|{attributes}|{File.GetLastWriteTimeUtc(path).Ticks}|{content}";
            }).ToArray();
    }

    public void ConvertDirectoryToJunction(string relative, string targetRelative)
    {
        byte[] target = Encoding.Unicode.GetBytes(@"\??\" + At(targetRelative));
        byte[] buffer = new byte[16 + target.Length + 4];
        BitConverter.GetBytes(0xA0000003u).CopyTo(buffer, 0);
        BitConverter.GetBytes((ushort)(buffer.Length - 8)).CopyTo(buffer, 4);
        BitConverter.GetBytes((ushort)target.Length).CopyTo(buffer, 10);
        BitConverter.GetBytes((ushort)(target.Length + 2)).CopyTo(buffer, 12);
        target.CopyTo(buffer, 16);
        using var handle = CreateFileW(At(relative), 0x100, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        Assert.False(handle.IsInvalid, $"Fixture attribute handle failed: {Marshal.GetLastWin32Error()}");
        bool converted = DeviceIoControl(handle, 0x000900A4, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero);
        Assert.True(converted, $"Fixture reparse conversion failed: {Marshal.GetLastWin32Error()}");
        _junctions.Add(At(relative));
    }

    public SafeFileHandle HoldRenameAccess(string relative)
    {
        var handle = CreateFileW(At(relative), 0x10000, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        Assert.False(handle.IsInvalid, $"Fixture rename handle failed: {Marshal.GetLastWin32Error()}");
        return handle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputLength,
        IntPtr output, int outputLength, out int returned, IntPtr overlapped);

    public void Dispose()
    {
        var fullRoot = Path.GetFullPath(Root);
        if (Path.GetDirectoryName(fullRoot) != Path.TrimEndingDirectorySeparator(Path.GetTempPath())
            || !Path.GetFileName(fullRoot).StartsWith("mim-inspector-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing cleanup outside the owned temporary fixture.");
        }

        foreach (var junction in _junctions)
        {
            Directory.Delete(junction);
        }

        Directory.Delete(fullRoot, recursive: true);
    }
}
