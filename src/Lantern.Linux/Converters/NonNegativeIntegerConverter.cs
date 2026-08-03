using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Lantern.Linux.Converters;

public sealed class NonNegativeIntegerConverter : IValueConverter
{
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        value?.ToString() ?? "0";

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        return value is string text &&
               int.TryParse(text, NumberStyles.Integer, culture, out var parsed) &&
               parsed >= 0
            ? parsed
            : BindingOperations.DoNothing;
    }
}
