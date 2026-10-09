using System.Xml.Linq;

namespace MinecraftInstanceMigration.App.Tests;

public sealed class MemberPresentationTests
{
    [Fact]
    public void MainWindowStartsWithMigrationAndOffersLanguageThemeAndNextStep()
    {
        XDocument window = XDocument.Load(Source("MainWindow.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        Assert.Equal("1", window.Descendants(wpf + "TabControl").Single().Attribute("SelectedIndex")?.Value);
        foreach (string id in new[] { "LanguageSelector", "ThemeSelector", "NextStep" })
        {
            Assert.Contains(window.Descendants(), element =>
                element.Attributes().Any(attribute => attribute.Name.LocalName == "AutomationProperties.AutomationId" && attribute.Value == id));
        }
    }

    [Theory]
    [InlineData("MigrationConfirmationWindow.xaml", "CancelMigrationStart", "ConfirmMigrationStart")]
    [InlineData("RollbackConfirmationWindow.xaml", "CancelRollback", "ConfirmRollback")]
    public void EnterDefaultsToCancelForFileChangingConfirmations(string file, string cancelId, string confirmId)
    {
        XDocument window = XDocument.Load(Source(file));
        XElement Find(string id) => window.Descendants().Single(element =>
            element.Attributes().Any(attribute => attribute.Name.LocalName == "AutomationProperties.AutomationId" && attribute.Value == id));
        Assert.Equal("True", Find(cancelId).Attribute("IsDefault")?.Value);
        Assert.NotEqual("True", Find(confirmId).Attribute("IsDefault")?.Value);
    }

    private static string Source(string file)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "MinecraftInstanceMigration.App", file);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Application source was not found.");
    }
}
