using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace MiHouse.App.Views;

// Static bodies inherit the page context through a plain visual parent. Only
// RoomPage supplies ItemsSource and uses the separate virtualizing ListView.
// Both hosts share the same header instance, padding and header/content gap.
public sealed partial class PageLayout : UserControl
{
    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(nameof(Heading), typeof(string), typeof(PageLayout), new PropertyMetadata(string.Empty, OnLayoutChanged));
    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(PageLayout), new PropertyMetadata(string.Empty, OnLayoutChanged));
    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(nameof(Body), typeof(UIElement), typeof(PageLayout), new PropertyMetadata(null, OnLayoutChanged));
    public static readonly DependencyProperty HeaderContentProperty = DependencyProperty.Register(nameof(HeaderContent), typeof(UIElement), typeof(PageLayout), new PropertyMetadata(null, OnLayoutChanged));
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(PageLayout), new PropertyMetadata(null, OnLayoutChanged));
    public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.Register(nameof(ItemTemplate), typeof(DataTemplate), typeof(PageLayout), new PropertyMetadata(null, OnLayoutChanged));
    private bool _usesItems;
    private readonly UISettings _uiSettings = new();
    private Storyboard? _entrance;
    private bool _entrancePending;
    public PageLayout()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => PageHeader.DataContext = DataContext;
        Loaded += (_, _) => { if (_entrancePending) { PlayEntrance(); } };
        Unloaded += (_, _) => ResetEntrance();
    }

    // Called only by the navigation host when a different page is presented.
    // Data bindings, refreshes and detail sheets never request an entrance.
    public void PlayEntrance()
    {
        ResetEntrance();
        if (!_uiSettings.AnimationsEnabled) { return; }
        if (!IsLoaded) { _entrancePending = true; return; }
        var duration = new Duration(TimeSpan.FromMilliseconds(200));
        var rise = new DoubleAnimation { From = 12, To = 0, Duration = duration, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var fade = new DoubleAnimation { From = 0, To = 1, Duration = duration, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(rise, EntranceOffset);
        Storyboard.SetTargetProperty(rise, "Y");
        Storyboard.SetTarget(fade, EntranceHost);
        Storyboard.SetTargetProperty(fade, "Opacity");
        _entrance = new Storyboard();
        _entrance.Children.Add(rise);
        _entrance.Children.Add(fade);
        _entrance.Begin();
    }

    private void ResetEntrance()
    {
        _entrancePending = false;
        _entrance?.Stop();
        _entrance = null;
        EntranceOffset.Y = 0;
        EntranceHost.Opacity = 1;
    }
    public string Heading { get => (string)GetValue(HeadingProperty); set => SetValue(HeadingProperty, value); }
    public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public UIElement? Body { get => (UIElement?)GetValue(BodyProperty); set => SetValue(BodyProperty, value); }
    public UIElement? HeaderContent { get => (UIElement?)GetValue(HeaderContentProperty); set => SetValue(HeaderContentProperty, value); }
    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public DataTemplate? ItemTemplate { get => (DataTemplate?)GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }
    public double ContentWidth => Math.Max(0, ActualWidth - PageStack.Padding.Left - PageStack.Padding.Right);
    private static void OnLayoutChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) => ((PageLayout)sender).UpdateLayoutContent(e.Property);
    private void UpdateLayoutContent(DependencyProperty property)
    {
        if (PageList is null) { return; }
        PageHeading.Text = Heading;
        PageSubtitle.Text = Subtitle;
        AutomationProperties.SetName(PageScroll, Heading);
        AutomationProperties.SetName(PageList, Heading);
        if (property == BodyProperty)
        {
            BodyHost.Children.Clear();
            if (Body is not null) { BodyHost.Children.Add(Body); }
        }
        if (property == HeaderContentProperty)
        {
            HeaderHost.Children.Clear();
            if (HeaderContent is not null) { HeaderHost.Children.Add(HeaderContent); }
        }
        if (property == ItemsSourceProperty || property == ItemTemplateProperty)
        {
            bool usesItems = ItemsSource is not null;
            if (usesItems != _usesItems)
            {
                if (usesItems)
                {
                    PageStack.Children.Remove(PageHeader);
                    PageHeader.Margin = new Thickness(0, 0, 0, PageStack.Spacing);
                    PageList.Header = PageHeader;
                }
                else
                {
                    PageList.Header = null;
                    PageHeader.Margin = new Thickness(0);
                    PageStack.Children.Insert(0, PageHeader);
                }
                PageHeader.DataContext = DataContext;
                _usesItems = usesItems;
                PageScroll.Visibility = usesItems ? Visibility.Collapsed : Visibility.Visible;
                PageList.Visibility = usesItems ? Visibility.Visible : Visibility.Collapsed;
            }
            PageList.ItemTemplate = ItemTemplate;
            PageList.ItemsSource = ItemsSource;
        }
    }
}
