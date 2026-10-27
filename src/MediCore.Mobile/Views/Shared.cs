using System.Globalization;

namespace MediCore.Mobile.Views;

/// <summary>Muestra un elemento solo si el texto no está vacío (mensajes de error de formularios).</summary>
public sealed class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Color de fondo y de texto de la etiqueta de estado de una cita ("Confirmada" en verde, "Cancelada" en rojo, como el diseño).</summary>
public sealed class StatusColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var cancelled = value is true;
        var text = parameter as string == "text";
        var key = (cancelled, text) switch
        {
            (true, true) => "BadgeRedText",
            (true, false) => "BadgeRed",
            (false, true) => "BadgeGreenText",
            _ => "BadgeGreen",
        };
        return Application.Current!.Resources.TryGetValue(key, out var color) ? color : Colors.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
