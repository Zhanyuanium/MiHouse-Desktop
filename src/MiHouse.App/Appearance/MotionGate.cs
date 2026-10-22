using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MiHouse.App.Appearance;

/// <summary>
/// Attached gate that forces <see cref="ProgressRing.IsActive"/> and
/// <see cref="ProgressBar.IsIndeterminate"/> to respect <see cref="AppMotion.IsEnabled"/>. Bind the
/// desired activity to <c>MotionGate.Active</c>/<c>MotionGate.Indeterminate</c> instead of the
/// control's own property; when motion is off the indicator freezes while its static label stays.
/// </summary>
public static class MotionGate
{
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.RegisterAttached(
        "Active", typeof(bool), typeof(MotionGate), new PropertyMetadata(false, OnActiveChanged));

    public static readonly DependencyProperty IndeterminateProperty = DependencyProperty.RegisterAttached(
        "Indeterminate", typeof(bool), typeof(MotionGate), new PropertyMetadata(false, OnIndeterminateChanged));

    private static readonly List<WeakReference<FrameworkElement>> _subscribers = [];

    static MotionGate()
    {
        AppMotion.Changed += RefreshAll;
    }

    public static bool GetActive(DependencyObject obj) => (bool)obj.GetValue(ActiveProperty);

    public static void SetActive(DependencyObject obj, bool value) => obj.SetValue(ActiveProperty, value);

    public static bool GetIndeterminate(DependencyObject obj) => (bool)obj.GetValue(IndeterminateProperty);

    public static void SetIndeterminate(DependencyObject obj, bool value) => obj.SetValue(IndeterminateProperty, value);

    private static void OnActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ProgressRing ring)
        {
            Subscribe(ring);
            Apply(ring);
        }
    }

    private static void OnIndeterminateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ProgressBar bar)
        {
            Subscribe(bar);
            Apply(bar);
        }
    }

    private static void Subscribe(FrameworkElement element)
    {
        element.Loaded -= OnLoaded;
        element.Unloaded -= OnUnloaded;
        element.Loaded += OnLoaded;
        element.Unloaded += OnUnloaded;
        if (element.IsLoaded)
        {
            Add(element);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            Add(element);
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            Remove(element);
        }
    }

    private static void Add(FrameworkElement element)
    {
        if (_subscribers.Any(weak => weak.TryGetTarget(out FrameworkElement? target) && ReferenceEquals(target, element)))
        {
            return;
        }

        _subscribers.Add(new WeakReference<FrameworkElement>(element));
    }

    private static void Remove(FrameworkElement element)
    {
        _subscribers.RemoveAll(weak => !weak.TryGetTarget(out FrameworkElement? target) || ReferenceEquals(target, element));
    }

    private static void RefreshAll()
    {
        for (int index = _subscribers.Count - 1; index >= 0; index--)
        {
            if (_subscribers[index].TryGetTarget(out FrameworkElement? element))
            {
                Apply(element);
            }
            else
            {
                _subscribers.RemoveAt(index);
            }
        }
    }

    private static void Apply(FrameworkElement element)
    {
        if (element is ProgressRing ring)
        {
            ring.IsActive = GetActive(ring) && AppMotion.IsEnabled;
        }
        else if (element is ProgressBar bar)
        {
            bar.IsIndeterminate = GetIndeterminate(bar) && AppMotion.IsEnabled;
        }
    }
}
