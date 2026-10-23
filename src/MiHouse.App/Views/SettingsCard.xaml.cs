using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace MiHouse.App.Views;

public sealed partial class SettingsCard : UserControl
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingsCard), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingsCard), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(SettingsCard), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty TrailingProperty = DependencyProperty.Register(nameof(Trailing), typeof(object), typeof(SettingsCard), new PropertyMetadata(null));
    public SettingsCard()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }
    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public object? Trailing { get => GetValue(TrailingProperty); set => SetValue(TrailingProperty, value); }

    /// <summary>
    /// Keeps the trailing control inside the card when the window gets narrow. WinUI 3 has no
    /// <c>ClipToBounds</c>, so the card clips itself with a rectangle that follows its size.
    /// </summary>
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (RootCard.ActualWidth <= 0 || RootCard.ActualHeight <= 0)
        {
            return;
        }

        RootCard.Clip = new RectangleGeometry { Rect = new Rect(0, 0, RootCard.ActualWidth, RootCard.ActualHeight) };
    }
}
