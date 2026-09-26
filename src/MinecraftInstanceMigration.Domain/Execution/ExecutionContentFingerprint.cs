namespace MinecraftInstanceMigration.Domain.Execution;

public sealed record ExecutionContentFingerprint
{
    public ExecutionContentFingerprint(
        int fileCount,
        int directoryCount,
        long totalBytes,
        string sha256)
    {
        if (fileCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileCount));
        }

        if (directoryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(directoryCount));
        }

        if (totalBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalBytes));
        }

        if (!IsSha256(sha256))
        {
            throw new ArgumentException("A SHA-256 fingerprint must be 64 hexadecimal characters.", nameof(sha256));
        }

        FileCount = fileCount;
        DirectoryCount = directoryCount;
        TotalBytes = totalBytes;
        Sha256 = sha256.ToUpperInvariant();
    }

    public int FileCount { get; }

    public int DirectoryCount { get; }

    public long TotalBytes { get; }

    public string Sha256 { get; }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character =>
            character is >= '0' and <= '9' or
            >= 'A' and <= 'F' or
            >= 'a' and <= 'f');
}
