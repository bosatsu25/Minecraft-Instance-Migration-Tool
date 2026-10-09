using System.ComponentModel;
using System.Windows;
using MinecraftInstanceMigration.App.Presentation;

namespace MinecraftInstanceMigration.App;

public partial class App : System.Windows.Application
{
    private readonly ThemeManager theme = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        UpdateTheme();
        UiPreferences.Current.PropertyChanged += PreferencesChanged;
        Activated += AppActivated;
        base.OnStartup(e);
    }

    private void PreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UiPreferences.ThemeIndex))
        {
            UpdateTheme();
        }
    }

    private void AppActivated(object? sender, EventArgs e) => UpdateTheme();

    private void UpdateTheme() => theme.Apply(Resources, UiPreferences.Current.ThemeIndex,
        SystemParameters.HighContrast, ThemeManager.WindowsUsesDarkTheme());

    protected override void OnExit(ExitEventArgs e)
    {
        UiPreferences.Current.PropertyChanged -= PreferencesChanged;
        Activated -= AppActivated;
        base.OnExit(e);
    }
}
