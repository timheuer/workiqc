using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace WorkIQC.App.Services;

internal static class ThemeBrushResolver
{
    public static SolidColorBrush GetBrush(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value)
            && value is SolidColorBrush brush)
        {
            return brush;
        }

        throw new InvalidOperationException($"Expected SolidColorBrush resource '{key}'.");
    }
}
