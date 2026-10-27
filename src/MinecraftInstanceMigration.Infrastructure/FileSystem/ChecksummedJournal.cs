using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;

namespace MinecraftInstanceMigration.Infrastructure.FileSystem;

// Shared mechanics of the durable JSON Lines journals: create-only no-follow files, bounded reads,
// torn-tail detection, SHA-256 record checksums and flushed appends. Each journal keeps its own
// schema and state machine; this type never interprets records.
internal static class ChecksummedJournal
{
    internal const int MaximumBytes = 256 * 1024;

    // Reading is strict: unknown, duplicate, or missing members fail closed. These settings do not
    // change serialized output, so checksums of journals written by earlier releases still match.
    internal static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectRequiredConstructorParameters = true,
    };

    internal static FileStream CreateNew(SafeFileHandle parent, string fileName)
    {
        (SafeFileHandle handle, int status) = NtFileApi.OpenRelative(
            parent,
            fileName,
            NtFileApi.FileReadData |
            NtFileApi.FileWriteData |
            NtFileApi.FileReadAttributes |
            NtFileApi.FileWriteAttributes |
            NtFileApi.Synchronize,
            NtFileApi.ShareRead,
            NtFileApi.FileCreate,
            NtFileApi.NoFollowOpenOptions);

        if (status != 0)
        {
            handle.Dispose();
            throw FileSystemErrors.ForCreate(status);
        }

        return ToRegularFileStream(handle, FileAccess.ReadWrite);
    }

    internal static FileStream OpenExisting(SafeFileHandle parent, string fileName, bool writable)
    {
        uint access = NtFileApi.FileReadData | NtFileApi.FileReadAttributes | NtFileApi.Synchronize;
        if (writable)
        {
            access |= NtFileApi.FileWriteData | NtFileApi.FileWriteAttributes;
        }

        (SafeFileHandle handle, int status) = NtFileApi.OpenRelative(
            parent,
            fileName,
            access,
            NtFileApi.ShareRead,
            NtFileApi.FileOpen,
            NtFileApi.NoFollowOpenOptions);

        if (status != 0)
        {
            handle.Dispose();
            throw FileSystemErrors.ForOpen(status);
        }

        return ToRegularFileStream(handle, writable ? FileAccess.ReadWrite : FileAccess.Read);
    }

    // A journal reference is "<local parent>\<prefix><32-hex id>.jsonl"; anything else is not ours.
    internal static bool TryResolveReference(
        string? id,
        string? journalPath,
        string fileNamePrefix,
        out string parent,
        out string fileName)
    {
        parent = "";
        fileName = "";

        if (!Guid.TryParseExact(id, "N", out _) || string.IsNullOrWhiteSpace(journalPath))
        {
            return false;
        }

        int separator = journalPath.LastIndexOf('\\');
        if (separator <= 2 || separator == journalPath.Length - 1)
        {
            return false;
        }

        fileName = journalPath[(separator + 1)..];
        return string.Equals(fileName, $"{fileNamePrefix}{id}.jsonl", StringComparison.OrdinalIgnoreCase) &&
            WindowsPathRules.TryNormalizeLocalDirectory(journalPath[..separator], out parent);
    }

    // Bytes after the final newline were never acknowledged and are reported as a torn tail.
    // A complete line that is malformed or fails its integrity check is never skipped.
    internal static JournalContents<TRecord> Read<TRecord>(FileStream stream, Func<TRecord, bool> isIntact)
        where TRecord : class
    {
        if (stream.Length <= 0 || stream.Length > MaximumBytes)
        {
            throw new JournalFormatException();
        }

        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.Position = 0;
        int readOffset = 0;
        while (readOffset < bytes.Length)
        {
            int read = stream.Read(bytes, readOffset, bytes.Length - readOffset);
            if (read == 0)
            {
                throw new JournalFormatException();
            }

            readOffset += read;
        }

        int lastNewline = Array.LastIndexOf(bytes, (byte)'\n');
        if (lastNewline < 0)
        {
            throw new JournalFormatException();
        }

        int validLength = lastNewline + 1;
        var records = new List<TRecord>();
        int start = 0;
        while (start < validLength)
        {
            int newline = Array.IndexOf(bytes, (byte)'\n', start, validLength - start);
            if (newline < 0)
            {
                throw new JournalFormatException();
            }

            int length = newline - start;
            if (length > 0 && bytes[newline - 1] == (byte)'\r')
            {
                length--;
            }

            if (length <= 0)
            {
                throw new JournalFormatException();
            }

            TRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<TRecord>(bytes.AsSpan(start, length), Options);
            }
            catch (JsonException)
            {
                throw new JournalFormatException();
            }

            if (record is null || !isIntact(record))
            {
                throw new JournalFormatException();
            }

            records.Add(record);
            start = newline + 1;
        }

        if (records.Count == 0)
        {
            throw new JournalFormatException();
        }

        return new JournalContents<TRecord>(records, validLength, validLength != bytes.Length);
    }

    internal static void PrepareAppend<TRecord>(FileStream stream, JournalContents<TRecord> contents)
    {
        if (contents.HasTornTail)
        {
            stream.SetLength(contents.ValidLength);
            stream.Position = contents.ValidLength;
            stream.Flush(flushToDisk: true);
        }
        else
        {
            stream.Position = contents.ValidLength;
        }
    }

    // A record is acknowledged only after its terminating newline has been flushed to disk.
    internal static void AppendDurably<TRecord>(FileStream stream, TRecord record)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(record, Options);
        stream.Write(json);
        stream.WriteByte((byte)'\n');
        stream.Flush(flushToDisk: true);
    }

    internal static string Checksum<TPayload>(TPayload payload) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload, Options)));

    internal static bool ChecksumMatches<TPayload>(string? checksum, TPayload payload) =>
        IsSha256Hex(checksum) &&
        string.Equals(checksum, Checksum(payload), StringComparison.OrdinalIgnoreCase);

    internal static bool IsSha256Hex(string? value) =>
        value is { Length: 64 } &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f');

    private static FileStream ToRegularFileStream(SafeFileHandle handle, FileAccess access)
    {
        try
        {
            FileAttributes attributes = NtFileApi.ReadAttributes(handle);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new FileSystemException(FileSystemFailureKind.ReparsePoint);
            }

            if (attributes.HasFlag(FileAttributes.Directory))
            {
                throw new FileSystemException(FileSystemFailureKind.NotRegularFile);
            }

            return new FileStream(handle, access, bufferSize: 4096, isAsync: false);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }
}

internal sealed record JournalContents<TRecord>(
    IReadOnlyList<TRecord> Records,
    int ValidLength,
    bool HasTornTail);

internal sealed class JournalFormatException : Exception;
