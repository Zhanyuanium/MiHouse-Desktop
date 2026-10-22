using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace MiHouse.App.Views;

public sealed partial class RoomSelectionBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is true
        ? Application.Current.Resources["DeviceIconBackdropBrush"]
        : new SolidColorBrush(Colors.Transparent);
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
