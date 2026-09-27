namespace MinecraftInstanceMigration.Application.Capacity;

public static class MigrationCapacityMarginPolicy
{
    public const long MinimumReserveBytes = 64L * 1024 * 1024;
    public const long MaximumReserveBytes = 1024L * 1024 * 1024;

    public static long RequiredBytes(long logicalBytes)
    {
        if (logicalBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalBytes));
        }

        long percentage = checked((logicalBytes + 19) / 20);
        long margin = Math.Clamp(
            percentage,
            MinimumReserveBytes,
            MaximumReserveBytes);
        return checked(logicalBytes + margin);
    }
}
