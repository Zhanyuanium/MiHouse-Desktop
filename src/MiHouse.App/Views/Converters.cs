using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
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

/// <summary>
/// Turns a device category key into a fresh <see cref="Geometry"/> for a <see cref="Microsoft.UI.Xaml.Controls.PathIcon"/>.
/// The path data lives as <c>x:String</c> resources in <c>Styles/DeviceIcons.xaml</c>; a WinUI geometry cannot be
/// shared across two elements, so each binding parses its own instance. Unknown categories fall back to
/// <c>DeviceIconPath.other</c>.
/// </summary>
public sealed partial class DeviceIconConverter : IValueConverter
{
    private const string _keyPrefix = "DeviceIconPath.";
    private const string _fallbackCategory = "other";

    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        string category = value as string is { Length: > 0 } text ? text.Trim().ToLowerInvariant() : _fallbackCategory;
        string? pathData = GetPathData(category) ?? GetPathData(_fallbackCategory);
        return pathData is null ? null : ParseGeometry(pathData);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();

    private static string? GetPathData(string category)
    {
        try
        {
            // The indexer resolves merged dictionaries; ResourceDictionary.TryGetValue is not
            // documented to do so, which would silently leave every icon blank.
            object? resource = Application.Current.Resources[_keyPrefix + category];
            return resource is string data && !string.IsNullOrWhiteSpace(data) ? data : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Geometry ParseGeometry(string pathData)
    {
        // Parse into a throwaway Path so the GeometryConverter applies, then detach the geometry
        // (Path.Data = null) so it has no owner. A shared/owned geometry would raise the
        // single-parent exception the moment a second PathIcon adopted it.
        string xaml = $"<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Path.Data>{pathData}</Path.Data></Path>";
        var path = (Microsoft.UI.Xaml.Shapes.Path)XamlReader.Load(xaml);
        Geometry geometry = path.Data;
        path.Data = null;
        return geometry;
    }
}
