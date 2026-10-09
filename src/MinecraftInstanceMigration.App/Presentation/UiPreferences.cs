using System.ComponentModel;

namespace MinecraftInstanceMigration.App.Presentation;

// Session-only choices: no settings file, user paths, or identifiers are persisted.
public sealed class UiPreferences : INotifyPropertyChanged
{
    public static UiPreferences Current { get; } = new();
    private int languageIndex;
    private int themeIndex;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int LanguageIndex
    {
        get => languageIndex;
        set
        {
            if (value is < 0 or > 1 || languageIndex == value)
            {
                return;
            }
            languageIndex = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LanguageIndex)));
        }
    }

    public int ThemeIndex
    {
        get => themeIndex;
        set
        {
            if (value is < 0 or > 2 || themeIndex == value)
            {
                return;
            }
            themeIndex = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThemeIndex)));
        }
    }
}
