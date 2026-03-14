using Microsoft.UI.Xaml.Data;

namespace WorkIQC.App.Converters;

public sealed class CheckGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? "\uE73E" : "\uE711"; // Checkmark : Cancel

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public sealed class CheckColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => new Microsoft.UI.Xaml.Media.SolidColorBrush(
            value is true
                ? Microsoft.UI.ColorHelper.FromArgb(255, 16, 185, 129)   // green
                : Microsoft.UI.ColorHelper.FromArgb(255, 232, 17, 35));  // red

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
