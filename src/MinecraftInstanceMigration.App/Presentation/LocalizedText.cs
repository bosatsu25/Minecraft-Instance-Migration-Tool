using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace MinecraftInstanceMigration.App.Presentation;

public sealed class TextExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(nameof(UiPreferences.LanguageIndex))
        {
            Source = UiPreferences.Current,
            Mode = BindingMode.OneWay,
            Converter = StaticTextConverter.Instance,
            ConverterParameter = Key,
        }.ProvideValue(serviceProvider);

    private sealed class StaticTextConverter : IValueConverter
    {
        internal static StaticTextConverter Instance { get; } = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            UiText.Translate(parameter as string ?? "", value is not int index || index == 0);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}

public sealed class LocalizedBindingExtension : MarkupExtension
{
    public string Path { get; set; } = "";
    public string? Format { get; set; }
    public string NullText { get; set; } = "Unknown";
    public BindingMode Mode { get; set; } = BindingMode.OneWay;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (Mode != BindingMode.OneWay)
        {
            throw new InvalidOperationException("Localized outputs must be read-only.");
        }
        var binding = new MultiBinding
        {
            Mode = BindingMode.OneWay,
            Converter = new RenderedTextConverter(Format, NullText),
        };
        binding.Bindings.Add(new Binding(Path) { Mode = BindingMode.OneWay });
        binding.Bindings.Add(new Binding(nameof(UiPreferences.LanguageIndex))
        {
            Source = UiPreferences.Current,
            Mode = BindingMode.OneWay,
        });
        return binding.ProvideValue(serviceProvider);
    }

    private sealed class RenderedTextConverter(string? format, string nullText) : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            object? value = values.Length == 0 ? null : values[0];
            string text = value is null || value == DependencyProperty.UnsetValue
                ? nullText
                : System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? nullText;
            if (format is not null)
            {
                text = string.Format(CultureInfo.InvariantCulture, format, text);
            }
            return UiText.Translate(text, values.Length < 2 || values[1] is not int index || index == 0);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            targetTypes.Select(_ => Binding.DoNothing).ToArray();
    }
}
