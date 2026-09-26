using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using MinecraftInstanceMigration.App;

namespace MinecraftInstanceMigration.UiTests;

[CollectionDefinition("UI smoke", DisableParallelization = true)]
public sealed class UiSmokeCollection;

[Collection("UI smoke")]
public sealed class InspectorSmokeTests
{
    [Fact]
    public void ApplicationStarts()
    {
        using var session = UiSession.Open();
        Assert.Equal("Minecraft Instance Migration", session.Window.Title);
        foreach (string id in new[] { "CandidatePath", "Inspect", "Cancel", "RootState", "Entries" })
        {
            Assert.NotNull(session.Find(id));
        }

        Assert.True(session.CloseGracefully(), "The main window did not close the application.");
    }

    [Fact]
    public void InspectKnownFixture()
    {
        using var fixture = new UiFixture();
        File.WriteAllText(fixture.At("options.txt"), "fixture-options");
        Directory.CreateDirectory(fixture.At("config"));
        Directory.CreateDirectory(fixture.At("saves"));
        string[] before = fixture.Snapshot();
        using var session = UiSession.Open();
        session.Inspect(fixture.Root);
        Assert.Contains("Directory", session.Find("RootState").Name);
        Assert.Equal(new[] { "options.txt", "File", "File", "True" }, session.RenderedEntryCells("options.txt"));
        Assert.Equal(new[] { "config", "Directory", "Directory", "True" }, session.RenderedEntryCells("config"));
        Assert.Equal(new[] { "saves", "Directory", "Directory", "True" }, session.RenderedEntryCells("saves"));
        Assert.Equal(new[] { "resourcepacks", "Directory", "Missing", "Unknown" },
            session.RenderedEntryCells("resourcepacks"));

        Assert.Equal(before, fixture.Snapshot());
    }

    [Fact]
    public void DisplaysExpectedKindMismatch()
    {
        using var fixture = new UiFixture();
        File.WriteAllText(fixture.At("config"), "fixture-file");
        string[] before = fixture.Snapshot();
        using var session = UiSession.Open();
        session.Inspect(fixture.Root);
        Assert.Equal(new[] { "config", "Directory", "File", "False" }, session.RenderedEntryCells("config"));

        Assert.Equal(before, fixture.Snapshot());
    }

    private sealed class UiSession : IDisposable
    {
        private readonly FlaUI.Core.Application application;
        private readonly UIA3Automation automation;
        private readonly int processId;
        public Window Window { get; }

        private UiSession(FlaUI.Core.Application application, UIA3Automation automation, Window window)
        {
            this.application = application;
            this.automation = automation;
            processId = application.ProcessId;
            Window = window;
        }

        public static UiSession Open()
        {
            var start = new ProcessStartInfo(Path.ChangeExtension(typeof(MainWindow).Assembly.Location, ".exe"))
            {
                UseShellExecute = false,
            };
            var application = FlaUI.Core.Application.Launch(start);
            int processId = application.ProcessId;
            UIA3Automation? automation = null;
            try
            {
                automation = new UIA3Automation();
                var window = application.GetMainWindow(automation, TimeSpan.FromSeconds(10));
                Assert.NotNull(window);
                return new UiSession(application, automation, window);
            }
            catch
            {
                StopProcess(processId);
                automation?.Dispose();
                application.Dispose();
                throw;
            }
        }

        public AutomationElement Find(string id) =>
            Window.FindFirstDescendant(cf => cf.ByAutomationId(id))
            ?? throw new InvalidOperationException($"Missing UI control: {id}");

        public void Inspect(string path)
        {
            Find("CandidatePath").AsTextBox().Text = path;
            Find("Inspect").AsButton().Invoke();
            var finished = Retry.WhileFalse(
                () => Find("Status").Name.StartsWith("Observation ", StringComparison.Ordinal),
                timeout: TimeSpan.FromSeconds(10), throwOnTimeout: false);
            Assert.True(finished.Success, "Inspector did not finish in ten seconds.");
        }

        public string[] RenderedEntryCells(string name) => Find("Entries")
            .FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.DataItem))
            .Single(row => row.Name.StartsWith($"EntryObservation {{ Name = {name},", StringComparison.Ordinal))
            .FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text))
            .Select(cell => cell.Name)
            .ToArray();

        public bool CloseGracefully()
        {
            using var process = Process.GetProcessById(processId);
            return process.CloseMainWindow() && process.WaitForExit(5000);
        }

        public void Dispose()
        {
            StopProcess(processId);
            automation.Dispose();
            application.Dispose();
        }

        private static void StopProcess(int processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (!process.HasExited)
                {
                    process.CloseMainWindow();
                    if (!process.WaitForExit(3000))
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(3000);
                    }
                }
            }
            catch (ArgumentException)
            {
                // The launched process already exited.
            }
        }
    }

    private sealed class UiFixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("mim-ui-").FullName;
        public string At(string name) => Path.Combine(Root, name);

        public string[] Snapshot() => Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories)
            .Prepend(Root)
            .Order(StringComparer.Ordinal)
            .Select(path =>
            {
                var attributes = File.GetAttributes(path);
                var content = attributes.HasFlag(FileAttributes.Directory)
                    ? "" : Convert.ToHexString(File.ReadAllBytes(path));
                return $"{Path.GetRelativePath(Root, path)}|{attributes}|{File.GetLastWriteTimeUtc(path).Ticks}|{content}";
            }).ToArray();

        public void Dispose()
        {
            string root = Path.GetFullPath(Root);
            if (Path.GetDirectoryName(root) != Path.TrimEndingDirectorySeparator(Path.GetTempPath()) ||
                !Path.GetFileName(root).StartsWith("mim-ui-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refusing cleanup outside the owned UI fixture.");
            }

            Directory.Delete(root, recursive: true);
        }
    }
}
