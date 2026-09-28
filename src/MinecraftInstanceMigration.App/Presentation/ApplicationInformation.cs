using System.Reflection;

namespace MinecraftInstanceMigration.App.Presentation;

public sealed record ApplicationInformation(
    string ProductName,
    string Version,
    string Platform,
    string Author)
{
    public static ApplicationInformation Current { get; } = FromAssembly(typeof(ApplicationInformation).Assembly);

    public static ApplicationInformation FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        string product = Required<AssemblyProductAttribute>(assembly, attribute => attribute.Product, "product");
        string version = Required<AssemblyInformationalVersionAttribute>(
            assembly,
            attribute => attribute.InformationalVersion,
            "informational version").Split('+', 2)[0];
        string author = Required<AssemblyCompanyAttribute>(assembly, attribute => attribute.Company, "author");
        string platform = Environment.Is64BitProcess ? "Windows x64" : "Unsupported architecture";

        return new ApplicationInformation(product, version, platform, author);
    }

    private static string Required<TAttribute>(
        Assembly assembly,
        Func<TAttribute, string> value,
        string label)
        where TAttribute : Attribute
    {
        TAttribute? attribute = assembly.GetCustomAttribute<TAttribute>();
        string? result = attribute is null ? null : value(attribute);
        if (string.IsNullOrWhiteSpace(result))
        {
            throw new InvalidOperationException($"Required application {label} metadata is unavailable.");
        }

        return result;
    }
}
