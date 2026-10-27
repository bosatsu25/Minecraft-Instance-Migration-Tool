namespace MinecraftInstanceMigration.Infrastructure.FileSystem;

// One lexical policy for every user-supplied path. It is input validation, not containment proof:
// adapters still open each component no-follow and compare canonical handle paths.
internal static class WindowsPathRules
{
    internal const int MaximumPathLength = 32000;

    private const string ForbiddenNameCharacters = "<>:\"/\\|?*";

    internal static bool IsSingleName(string? name) =>
        !string.IsNullOrEmpty(name) &&
        name is not "." and not ".." &&
        !name.EndsWith('.') &&
        !name.EndsWith(' ') &&
        name.All(character => character >= 32 && !ForbiddenNameCharacters.Contains(character));

    // Accepts only "X:\name\name" absolute local-drive syntax and returns it without empty components.
    // No filesystem access, so the rule can be tested on any platform.
    internal static bool TryNormalizeLexically(
        string? path,
        bool allowDriveRoot,
        out string normalized)
    {
        normalized = "";

        if (string.IsNullOrWhiteSpace(path) ||
            path.Length < 3 ||
            path.Length > MaximumPathLength ||
            !char.IsAsciiLetter(path[0]) ||
            path[1] != ':' ||
            path[2] != '\\')
        {
            return false;
        }

        string[] components = path[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (components.Length == 0 && !allowDriveRoot ||
            components.Any(component => !IsSingleName(component)))
        {
            return false;
        }

        normalized = path[..3] + string.Join('\\', components);
        return true;
    }

    // Lexical rule plus rejection of network drives. A drive that cannot be classified is rejected.
    internal static bool TryNormalizeLocalDirectory(string? path, out string normalized) =>
        TryNormalizeLocal(path, allowDriveRoot: false, out normalized);

    internal static bool TryNormalizeLocal(string? path, bool allowDriveRoot, out string normalized)
    {
        if (!TryNormalizeLexically(path, allowDriveRoot, out string candidate) ||
            IsNetworkDrive(candidate[..3]))
        {
            normalized = "";
            return false;
        }

        normalized = candidate;
        return true;
    }

    internal static bool IsEqualOrDescendant(string candidate, string root) =>
        string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase) ||
        candidate.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    internal static bool RootsOverlap(string first, string second) =>
        IsEqualOrDescendant(first, second) || IsEqualOrDescendant(second, first);

    private static bool IsNetworkDrive(string driveRoot)
    {
        try
        {
            return new DriveInfo(driveRoot).DriveType == DriveType.Network;
        }
        catch (Exception)
        {
            // Fail closed: an unclassifiable drive is never treated as local.
            return true;
        }
    }
}
