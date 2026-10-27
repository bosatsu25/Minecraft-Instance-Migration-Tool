using System.ComponentModel;

namespace MinecraftInstanceMigration.Infrastructure.FileSystem;

internal enum FileSystemFailureKind
{
    InvalidPath,
    OverlappingRoots,
    Missing,
    Changed,
    Collision,
    ReparsePoint,
    AccessDenied,
    NotRegularFile,
    IoFailure,
}

// Carries only a failure category: native messages can contain private user paths.
internal sealed class FileSystemException(FileSystemFailureKind kind) : Exception
{
    internal FileSystemFailureKind Kind { get; } = kind;
}

internal static class FileSystemErrors
{
    private const uint ErrorFileNotFound = 2;
    private const uint ErrorPathNotFound = 3;
    private const uint ErrorAccessDenied = 5;
    private const uint ErrorFileExists = 80;
    private const uint ErrorInvalidName = 123;
    private const uint ErrorBadPathName = 161;
    private const uint ErrorAlreadyExists = 183;
    private const uint ErrorFilenameExceedsRange = 206;

    internal static FileSystemException ForOpen(int status) => new(ClassifyOpen(status));

    internal static FileSystemException ForCreate(int status)
    {
        if (status != NtFileApi.StatusReparsePointEncountered &&
            NtFileApi.ToDosError(status) is ErrorFileExists or ErrorAlreadyExists)
        {
            return new FileSystemException(FileSystemFailureKind.Collision);
        }

        return ForOpen(status);
    }

    internal static FileSystemFailureKind ForEnumeration(int status) =>
        status == NtFileApi.StatusReparsePointEncountered
            ? FileSystemFailureKind.ReparsePoint
            : NtFileApi.ToDosError(status) == ErrorAccessDenied
                ? FileSystemFailureKind.AccessDenied
                : FileSystemFailureKind.IoFailure;

    internal static FileSystemFailureKind ForWin32Error(int error) =>
        error == ErrorAccessDenied
            ? FileSystemFailureKind.AccessDenied
            : FileSystemFailureKind.IoFailure;

    // Every adapter receives handle IO failures as one of these exception types. Anything else is a
    // programming error and must propagate instead of being reported as an expected IO outcome.
    internal static bool TryClassify(Exception error, out FileSystemFailureKind kind)
    {
        switch (error)
        {
            case FileSystemException fileSystem:
                kind = fileSystem.Kind;
                return true;
            case UnauthorizedAccessException:
                kind = FileSystemFailureKind.AccessDenied;
                return true;
            case Win32Exception win32:
                kind = ForWin32Error(win32.NativeErrorCode);
                return true;
            case IOException:
                kind = FileSystemFailureKind.IoFailure;
                return true;
            default:
                kind = FileSystemFailureKind.IoFailure;
                return false;
        }
    }

    private static FileSystemFailureKind ClassifyOpen(int status)
    {
        if (status == NtFileApi.StatusReparsePointEncountered)
        {
            return FileSystemFailureKind.ReparsePoint;
        }

        return NtFileApi.ToDosError(status) switch
        {
            ErrorFileNotFound or ErrorPathNotFound => FileSystemFailureKind.Missing,
            ErrorAccessDenied => FileSystemFailureKind.AccessDenied,
            ErrorFileExists or ErrorAlreadyExists => FileSystemFailureKind.Changed,
            ErrorInvalidName or ErrorBadPathName or ErrorFilenameExceedsRange => FileSystemFailureKind.InvalidPath,
            _ => FileSystemFailureKind.IoFailure,
        };
    }
}
