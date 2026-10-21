using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace MiHouse.App.Views;

public sealed partial class RoomPillBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool selected = value is true;
        if (parameter as string == "Foreground")
        {
            return Application.Current.Resources[selected ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorPrimaryBrush"];
        }

        return selected ? Application.Current.Resources["AccentFillColorDefaultBrush"] : new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
