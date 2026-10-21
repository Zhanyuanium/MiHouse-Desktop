using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

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
    public PageLayout()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => PageHeader.DataContext = DataContext;
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
