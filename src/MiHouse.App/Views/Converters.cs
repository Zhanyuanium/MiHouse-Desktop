using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MiHouse.App.Views;

public sealed partial class BooleanVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool visible = value is true;
        if (parameter is string text && text == "Inverse")
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed partial class HasTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is string text && !string.IsNullOrWhiteSpace(text);
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed partial class QrImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language) =>
        value is string text && Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) ? new BitmapImage(uri) : null;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed partial class OnlineOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is true ? 1d : 0.25d;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
