using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace MiHouse.App.Views;

public sealed partial class ResponsiveGridPanel : Panel
{
    public static readonly DependencyProperty MinimumItemWidthProperty = DependencyProperty.Register(
        nameof(MinimumItemWidth), typeof(double), typeof(ResponsiveGridPanel), new PropertyMetadata(280d, OnLayoutChanged));
    public static readonly DependencyProperty MaximumColumnsProperty = DependencyProperty.Register(
        nameof(MaximumColumns), typeof(int), typeof(ResponsiveGridPanel), new PropertyMetadata(3, OnLayoutChanged));
    public double MinimumItemWidth
    {
        get => (double)GetValue(MinimumItemWidthProperty);
        set => SetValue(MinimumItemWidthProperty, value);
    }

    public int MaximumColumns
    {
        get => (int)GetValue(MaximumColumnsProperty);
        set => SetValue(MaximumColumnsProperty, value);
    }

    private int Columns(double width) => Math.Clamp((int)Math.Floor((width + 12) / (Math.Max(1, MinimumItemWidth) + 12)), 1, Math.Max(1, MaximumColumns));
    private static void OnLayoutChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((ResponsiveGridPanel)sender).InvalidateMeasure();

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : 640;
        int columns = Columns(width);
        double itemWidth = Math.Max(0, (width - (columns - 1) * 12) / columns);
        double height = 0;
        for (int i = 0; i < Children.Count; i += columns)
        {
            double rowHeight = 0;
            for (int j = i; j < Math.Min(i + columns, Children.Count); j++)
            {
                Children[j].Measure(new Size(itemWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, Children[j].DesiredSize.Height);
            }

            height += rowHeight + (i == 0 ? 0 : 12);
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int columns = Columns(finalSize.Width);
        double itemWidth = Math.Max(0, (finalSize.Width - (columns - 1) * 12) / columns);
        double y = 0;
        for (int i = 0; i < Children.Count; i += columns)
        {
            double rowHeight = Children.Skip(i).Take(columns).Max(child => child.DesiredSize.Height);
            for (int j = i; j < Math.Min(i + columns, Children.Count); j++)
            {
                Children[j].Arrange(new Rect((j - i) * (itemWidth + 12), y, itemWidth, rowHeight));
            }

            y += rowHeight + 12;
        }

        return finalSize;
    }
}
