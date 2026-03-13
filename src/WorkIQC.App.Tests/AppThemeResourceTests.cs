using System.Xml.Linq;

namespace WorkIQC.App.Tests;

[TestClass]
public sealed class AppThemeResourceTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string AppXamlPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "WorkIQC.App",
        "App.xaml"));

    [TestMethod]
    public void AppXaml_DefinesCriticalThemeKeysForLightAndDark()
    {
        var document = XDocument.Load(AppXamlPath);
        var lightTheme = GetThemeDictionary(document, "Light");
        var darkTheme = GetThemeDictionary(document, "Dark");
        var requiredKeys = new[]
        {
            "AccentColor",
            "RailBackgroundColor",
            "SurfaceBackgroundColor",
            "SurfaceSecondaryBackgroundColor",
            "SurfaceElevatedBackgroundColor",
            "ShellPrimaryTextColor",
            "ShellSecondaryTextColor",
            "TextPrimaryBrush",
            "TextSecondaryBrush"
        };

        foreach (var key in requiredKeys)
        {
            Assert.IsTrue(ContainsKey(lightTheme, key), $"Light theme is missing '{key}'.");
            Assert.IsTrue(ContainsKey(darkTheme, key), $"Dark theme is missing '{key}'.");
        }
    }

    [TestMethod]
    public void AppXaml_UsesDistinctLightAndDarkSurfaceColors()
    {
        var document = XDocument.Load(AppXamlPath);
        var lightColors = ReadThemeColors(GetThemeDictionary(document, "Light"));
        var darkColors = ReadThemeColors(GetThemeDictionary(document, "Dark"));

        Assert.AreNotEqual(lightColors["RailBackgroundColor"], darkColors["RailBackgroundColor"]);
        Assert.AreNotEqual(lightColors["SurfaceBackgroundColor"], darkColors["SurfaceBackgroundColor"]);
        Assert.AreNotEqual(lightColors["ShellPrimaryTextColor"], darkColors["ShellPrimaryTextColor"]);
        Assert.AreNotEqual(lightColors["SuggestionAccentColor"], darkColors["SuggestionAccentColor"]);
    }

    private static XElement GetThemeDictionary(XDocument document, string key)
        => document
            .Descendants()
            .Single(element => element.Name.LocalName == "ResourceDictionary" && (string?)element.Attribute(XamlNamespace + "Key") == key);

    private static bool ContainsKey(XElement dictionary, string key)
        => dictionary
            .Elements()
            .Any(element => (string?)element.Attribute(XamlNamespace + "Key") == key);

    private static Dictionary<string, string> ReadThemeColors(XElement dictionary)
        => dictionary
            .Elements()
            .Where(element => element.Name.LocalName == "Color")
            .ToDictionary(
                element => (string?)element.Attribute(XamlNamespace + "Key") ?? string.Empty,
                element => (element.Value ?? string.Empty).Trim(),
                StringComparer.Ordinal);
}
