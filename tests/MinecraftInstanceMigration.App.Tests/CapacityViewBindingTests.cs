namespace MinecraftInstanceMigration.App.Tests;

public sealed class CapacityViewBindingTests
{
    [Fact]
    public void CapacityOutputsUseExplicitOneWayBindings()
    {
        string xaml = File.ReadAllText(FindRepositoryFile(
            "src",
            "MinecraftInstanceMigration.App",
            "MainWindow.xaml"));

        foreach (string property in new[]
        {
            "CapacityStatus",
            "CapacitySummary",
            "CapacityVolumeSummary",
            "CopySize",
            "ReplaceWriteSize",
            "BackupSize",
            "DestinationFreeSpace",
            "SafetyWorkspaceFreeSpace",
        })
        {
            Assert.Contains($"ui:LocalizedBinding Path={property}, Mode=OneWay", xaml, StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository source file was not found.");
    }
}
