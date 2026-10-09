using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace MinecraftInstanceMigration.App.Presentation;

public sealed class ThemeManager
{
    private ResourceDictionary? palette;
    private string? currentMode;

    public void Apply(ResourceDictionary resources, int selection, bool highContrast, bool windowsDark)
    {
        string mode = highContrast ? "contrast" : selection == 2 || selection == 0 && windowsDark ? "dark" : "light";
        if (!highContrast && currentMode == mode && palette is not null && resources.MergedDictionaries.Contains(palette))
        {
            return;
        }

        var next = new ResourceDictionary();
        bool dark = mode == "dark";
        Brush Color(string value)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            brush.Freeze();
            return brush;
        }
        next["PageBrush"] = highContrast ? SystemColors.WindowBrush : Color(dark ? "#161B22" : "#F4F6F8");
        next["SurfaceBrush"] = highContrast ? SystemColors.WindowBrush : Color(dark ? "#222A35" : "#FFFFFF");
        next["AlternateBrush"] = highContrast ? SystemColors.WindowBrush : Color(dark ? "#293342" : "#F1F5F9");
        next["TextBrush"] = highContrast ? SystemColors.WindowTextBrush : Color(dark ? "#EEF2F7" : "#17253A");
        next["MutedBrush"] = highContrast ? SystemColors.WindowTextBrush : Color(dark ? "#BECBDD" : "#46556A");
        next["BorderBrush"] = highContrast ? SystemColors.WindowTextBrush : Color(dark ? "#607087" : "#9DAABC");
        next["AccentBrush"] = highContrast ? SystemColors.HighlightBrush : Color(dark ? "#285AB5" : "#2353A6");
        next["AccentTextBrush"] = highContrast ? SystemColors.HighlightTextBrush : Color("#FFFFFF");
        next["HoverBrush"] = highContrast ? SystemColors.HighlightBrush : Color(dark ? "#34465F" : "#DDE9FA");
        next["WarningBrush"] = highContrast ? SystemColors.WindowBrush : Color(dark ? "#3A3020" : "#FFF5DE");
        next[SystemColors.WindowBrushKey] = next["SurfaceBrush"];
        next[SystemColors.WindowTextBrushKey] = next["TextBrush"];
        next[SystemColors.ControlBrushKey] = next["SurfaceBrush"];
        next[SystemColors.ControlTextBrushKey] = next["TextBrush"];
        next[SystemColors.HighlightBrushKey] = next["AccentBrush"];
        next[SystemColors.HighlightTextBrushKey] = next["AccentTextBrush"];

        int index = palette is null ? -1 : resources.MergedDictionaries.IndexOf(palette);
        if (index < 0)
        {
            resources.MergedDictionaries.Insert(0, next);
        }
        else
        {
            resources.MergedDictionaries[index] = next;
        }
        palette = next;
        currentMode = mode;
    }

    public static bool WindowsUsesDarkTheme()
    {
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1) is int value && value == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
