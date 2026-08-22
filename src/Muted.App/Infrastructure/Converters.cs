using System.Globalization;
using System.Windows;
using System.Windows.Data;

using Binding = System.Windows.Data.Binding;

namespace Muted.App.Infrastructure;

/// <summary>Shows an element only while the bound flag is false.</summary>
internal sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}

internal sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}

/// <summary>Two-way match against an enum member named by the converter parameter.</summary>
internal sealed class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is string name &&
        string.Equals(value.ToString(), name, StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter is not string name)
        {
            return Binding.DoNothing;
        }

        var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return enumType.IsEnum && Enum.TryParse(enumType, name, out var parsed)
            ? parsed
            : Binding.DoNothing;
    }
}

internal sealed class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is string name &&
        string.Equals(value.ToString(), name, StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>Maps a 0-1 amplitude onto the same dB scale the level meters draw.</summary>
internal sealed class DecibelPositionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var amplitude = value is double number ? number : 0d;
        var position = Controls.LevelMeter.Position(amplitude);
        return parameter is string raw && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale)
            ? position * scale
            : position;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
