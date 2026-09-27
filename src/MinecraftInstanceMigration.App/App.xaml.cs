using System.IO;
using System.Windows.Threading;

namespace MinecraftInstanceMigration.App;

public partial class App : System.Windows.Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs args) =>
        WriteCrash(args.Exception);

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs args) =>
        WriteCrash(args.ExceptionObject);

    private static void WriteCrash(object? error)
    {
        string? path = Environment.GetEnvironmentVariable("MIM_CRASH_LOG");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.WriteAllText(path, error?.ToString() ?? "<null>");
        }
        catch
        {
            // Diagnostics must never mask the original crash.
        }
    }
}
