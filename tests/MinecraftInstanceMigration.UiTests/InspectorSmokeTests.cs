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
        foreach (string id in new[] { "CandidatePath", "Inspect", "Cancel", "RootState", "Entries", "PreviewTab", "ReportTab" })
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
        Assert.Equal(new[] { "options.txt", "File", "File", "True" }, session.RenderedEntryCells("Entries", "options.txt"));
        Assert.Equal(new[] { "config", "Directory", "Directory", "True" }, session.RenderedEntryCells("Entries", "config"));
        Assert.Equal(new[] { "saves", "Directory", "Directory", "True" }, session.RenderedEntryCells("Entries", "saves"));
        Assert.Equal(new[] { "resourcepacks", "Directory", "Missing", "Unknown" },
            session.RenderedEntryCells("Entries", "resourcepacks"));

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
        Assert.Equal(new[] { "config", "Directory", "File", "False" },
            session.RenderedEntryCells("Entries", "config"));

        Assert.Equal(before, fixture.Snapshot());
    }

    [Fact]
    public void GeneratesReadOnlyMigrationPreview()
    {
        using var source = new UiFixture();
        using var destination = new UiFixture();
        Directory.CreateDirectory(source.At("config"));
        Directory.CreateDirectory(source.At("saves"));
        string[] sourceBefore = source.Snapshot();
        string[] destinationBefore = destination.Snapshot();

        using var session = UiSession.Open();
        session.SelectPreviewTab();
        session.GeneratePreview(source.Root, destination.Root);

        string[] config = session.RenderedEntryCells("PreviewEntries", "config");
        Assert.Contains("Copy", config);
        Assert.Contains("ReadyToCopy", config);
        Assert.Contains("Directory", config);
        Assert.Contains("Missing", config);

        string[] saves = session.RenderedEntryCells("PreviewEntries", "saves");
        Assert.Contains("Excluded", saves);
        Assert.Contains("ExcludedBySelection", saves);

        Assert.Equal(sourceBefore, source.Snapshot());
        Assert.Equal(destinationBefore, destination.Snapshot());
    }

    [Fact]
    public void ExposesConflictEditingControlsWithoutWritingFiles()
    {
        using var source = new UiFixture();
        using var destination = new UiFixture();
        Directory.CreateDirectory(source.At("config"));
        Directory.CreateDirectory(destination.At("config"));
        string[] sourceBefore = source.Snapshot();
        string[] destinationBefore = destination.Snapshot();

        using var session = UiSession.Open();
        session.SelectPreviewTab();
        session.GeneratePreview(source.Root, destination.Root);

        string[] config = session.RenderedEntryCells("PreviewEntries", "config");
        Assert.Contains("NeedsDecision", config);
        Assert.Contains("DestinationConflict", config);
        Assert.NotNull(session.Find("ApplyPreviewChoices"));
        Assert.NotNull(session.Find("ResetRecommendedChoices"));
        Assert.NotNull(session.Find("SelectAllChoices"));
        Assert.NotNull(session.Find("SelectNoChoices"));
        Assert.NotNull(session.Find("IncludeSelectedEntry"));
        Assert.NotNull(session.Find("ExcludeSelectedEntry"));
        Assert.NotNull(session.Find("SkipSelectedConflict"));
        Assert.NotNull(session.Find("ReplaceSelectedConflict"));
        Assert.NotNull(session.Find("ClearSelectedConflict"));
        Assert.Contains(
            "hanemod-client.json",
            session.Find("PreviewContentRules").Name,
            StringComparison.Ordinal);

        Assert.Equal(sourceBefore, source.Snapshot());
        Assert.Equal(destinationBefore, destination.Snapshot());
    }

    [Fact]
    public void ExecutesConfirmedCopyOnlyMigration()
    {
        using var source = new UiFixture();
        using var destination = new UiFixture();
        using var workspace = new UiFixture();
        using var unrelated = new UiFixture();
        Directory.CreateDirectory(source.At("config"));
        File.WriteAllText(source.At(Path.Combine("config", "sample.txt")), "sample-content");
        File.WriteAllText(unrelated.At("untouched.txt"), "do-not-change");
        string[] sourceBefore = source.Snapshot();
        string[] unrelatedBefore = unrelated.Snapshot();

        using var session = UiSession.Open();
        session.SelectPreviewTab();
        session.GeneratePreview(source.Root, destination.Root);

        session.ExecuteMigration(workspace.Root);

        Assert.Contains("Completed", session.Find("ExecutionState").Name);
        session.SelectReportTab();
        Assert.Contains("Completed", session.Find("ReportOverallOutcome").Name);
        Assert.Contains("Copy: 1", session.Find("ReportMigrationSummary").Name);
        Assert.Contains("verification: Succeeded", session.Find("ReportExecutionSummary").Name);
        Assert.Equal("sample-content", File.ReadAllText(destination.At(Path.Combine("config", "sample.txt"))));
        Assert.Equal(sourceBefore, source.Snapshot());
        Assert.Equal(unrelatedBefore, unrelated.Snapshot());
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
                timeout: TimeSpan.FromSeconds(10), throwOnTimeout: false, ignoreException: true);
            Assert.True(finished.Success, "Inspector did not finish in ten seconds.");
        }

        public void SelectPreviewTab()
        {
            Find("PreviewTab").AsTabItem().Select();
            var visible = Retry.WhileTrue(
                () => Window.FindFirstDescendant(cf => cf.ByAutomationId("PreviewSourcePath")) is null,
                timeout: TimeSpan.FromSeconds(5), throwOnTimeout: false, ignoreException: true);
            Assert.True(visible.Success, "Preview tab did not become available.");
        }

        public void SelectReportTab()
        {
            Find("ReportTab").AsTabItem().Select();
            var visible = Retry.WhileTrue(
                () => Window.FindFirstDescendant(cf => cf.ByAutomationId("ReportOverallOutcome")) is null,
                timeout: TimeSpan.FromSeconds(5), throwOnTimeout: false, ignoreException: true);
            Assert.True(visible.Success, "Report tab did not become available.");
        }

        public void GeneratePreview(string sourcePath, string destinationPath)
        {
            Find("PreviewSourcePath").AsTextBox().Text = sourcePath;
            Find("PreviewDestinationPath").AsTextBox().Text = destinationPath;

            Button generate = Find("GeneratePreview").AsButton();
            var ready = Retry.WhileFalse(
                () => generate.IsEnabled,
                timeout: TimeSpan.FromSeconds(5),
                throwOnTimeout: false,
                ignoreException: true);
            Assert.True(ready.Success, "Generate Preview did not become enabled.");

            generate.Invoke();

            var finished = Retry.WhileFalse(
                () =>
                {
                    try
                    {
                        return RenderedEntryCells("PreviewEntries", "config").Length > 0;
                    }
                    catch
                    {
                        return false;
                    }
                },
                timeout: TimeSpan.FromSeconds(15),
                throwOnTimeout: false,
                ignoreException: true);
            Assert.True(finished.Success, "Dry-run preview did not render the config row in fifteen seconds.");
        }

        public void ExecuteMigration(string workspacePath)
        {
            Find("SafetyWorkspacePath").AsTextBox().Text = workspacePath;
            Button checkCapacity = Find("CheckCapacity").AsButton();
            var canCheck = Retry.WhileFalse(
                () => checkCapacity.IsEnabled,
                timeout: TimeSpan.FromSeconds(5),
                throwOnTimeout: false,
                ignoreException: true);
            Assert.True(canCheck.Success, "Capacity check did not become enabled.");

            checkCapacity.Invoke();
            var capacityReady = Retry.WhileFalse(
                () => Find("CapacityStatus").Name.Contains("Ready", StringComparison.Ordinal),
                timeout: TimeSpan.FromSeconds(20),
                throwOnTimeout: false,
                ignoreException: true);
            Assert.True(capacityReady.Success, "Capacity preflight did not reach Ready in twenty seconds.");

            Button execute = Find("ExecuteMigration").AsButton();
            var ready = Retry.WhileFalse(
                () => execute.IsEnabled,
                timeout: TimeSpan.FromSeconds(5),
                throwOnTimeout: false,
                ignoreException: true);
            Assert.True(ready.Success, "Execute migration did not become enabled.");

            execute.Invoke();
            Window? confirmation = null;
            var shown = Retry.WhileFalse(
                () =>
                {
                    confirmation = Window.ModalWindows
                        .FirstOrDefault(candidate => candidate.Title == "Confirm migration");
                    return confirmation is not null;
                },
                timeout: TimeSpan.FromSeconds(5),
                throwOnTimeout: false,
                ignoreException: true);
            Assert.True(shown.Success, "Migration confirmation did not appear.");
            Assert.NotNull(confirmation);

            AutomationElement start = confirmation.FindFirstDescendant(
                cf => cf.ByAutomationId("ConfirmMigrationStart"))
                ?? throw new InvalidOperationException("Confirmation start button was not available.");
            start.AsButton().Invoke();

            var completed = Retry.WhileFalse(
                () => Find("ExecutionState").Name.Contains("Completed", StringComparison.Ordinal),
                timeout: TimeSpan.FromSeconds(20),
                throwOnTimeout: false,
                ignoreException: true);
            Assert.True(completed.Success, "Confirmed migration did not reach Completed in twenty seconds.");
        }

        public string[] RenderedEntryCells(string gridId, string name)
        {
            string[][] rows = Find(gridId)
                .FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.DataItem))
                .Select(row => row
                    .FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text))
                    .Select(cell => cell.Name)
                    .ToArray())
                .ToArray();

            return Assert.Single(rows, cells => cells.Contains(name, StringComparer.Ordinal));
        }

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
