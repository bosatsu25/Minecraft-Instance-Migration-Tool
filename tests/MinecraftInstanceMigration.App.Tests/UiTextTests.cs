using MinecraftInstanceMigration.App.Presentation;

namespace MinecraftInstanceMigration.App.Tests;

public sealed class UiTextTests
{
    [Fact]
    public void RepeatedThemeSwitchesKeepOnePaletteAndRestoreColors()
    {
        var resources = new System.Windows.ResourceDictionary();
        var styles = new System.Windows.ResourceDictionary();
        resources.MergedDictionaries.Add(styles);
        var themes = new ThemeManager();
        for (int index = 0; index < 100; index++)
        {
            themes.Apply(resources, 2, false, false);
            Assert.Equal("#FF161B22", resources["PageBrush"].ToString());
            themes.Apply(resources, 1, false, true);
            Assert.Equal("#FFF4F6F8", resources["PageBrush"].ToString());
            themes.Apply(resources, 0, false, true);
            Assert.Equal("#FF161B22", resources["PageBrush"].ToString());
            Assert.Equal(2, resources.MergedDictionaries.Count);
            Assert.Same(styles, resources.MergedDictionaries[1]);
        }
    }

    [Theory]
    [InlineData("Failed", "失敗")]
    [InlineData("Completed", "完了")]
    [InlineData("RecoveryRequired", "復旧が必要")]
    [InlineData("Unknown", "不明")]
    [InlineData("True", "はい")]
    [InlineData("Copy: 2; Replace: 1; Skip: 0", "コピー: 2; 置換: 1; スキップ: 0")]
    public void JapaneseTranslatesOutcomesAndCountSummaries(string english, string japanese)
    {
        Assert.Equal(japanese, UiText.Translate(english, japanese: true));
        Assert.Equal(english, UiText.Translate(english, japanese: false));
    }

    [Fact]
    public void FailureDetailRemainsFailureAndUnknownTextHasSafeFallback()
    {
        string message = UiText.Translate("Migration failed (IOException). Review recovery evidence before retrying.", true);
        Assert.Contains("失敗", message, StringComparison.Ordinal);
        Assert.Contains("復旧", message, StringComparison.Ordinal);
        Assert.DoesNotContain("完了", message, StringComparison.Ordinal);
        Assert.Equal("unrecognised-value", UiText.Translate("unrecognised-value", true));
    }

    [Fact]
    public void PhraseTranslationPreservesIdentifiersAndBoundsInput()
    {
        Assert.Equal("MyCompleted_file", UiText.Translate("MyCompleted_file", true));
        Assert.Equal("value完了suffix", UiText.Translate("value完了suffix", true));
        string large = new('x', 8193);
        Assert.Equal(large, UiText.Translate(large, true));
    }

    [Fact]
    public void PreferencesAcceptOnlySupportedValuesAndNotifyWithoutPersistence()
    {
        var preferences = new UiPreferences();
        var changed = new List<string?>();
        preferences.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.Equal(0, preferences.LanguageIndex);
        Assert.Equal(0, preferences.ThemeIndex);
        preferences.LanguageIndex = 1;
        preferences.ThemeIndex = 2;
        preferences.LanguageIndex = -1;
        preferences.ThemeIndex = 100;
        Assert.Equal(1, preferences.LanguageIndex);
        Assert.Equal(2, preferences.ThemeIndex);
        Assert.Equal(new[] { "LanguageIndex", "ThemeIndex" }, changed);
    }
}
