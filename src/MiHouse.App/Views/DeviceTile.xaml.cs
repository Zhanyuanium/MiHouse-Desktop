using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MiHouse.App.Appearance;
using MiHouse.App.ViewModels;

namespace MiHouse.App.Views;

public sealed partial class DeviceTile : UserControl
{
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(nameof(IsCompact), typeof(bool),
        typeof(DeviceTile), new PropertyMetadata(false, OnCompactChanged));
    public DeviceTile()
    {
        InitializeComponent();
        ElementCompositionPreview.GetElementVisual(HoverOverlay).Opacity = 0;
    }
    private DeviceRowViewModel? _observedDevice;
    public event EventHandler<DeviceRowViewModel>? DeviceOpened;
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    private static void OnCompactChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((DeviceTile)sender).UpdateLayoutMode();
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ObserveDevice();
        ElementCompositionPreview.GetElementVisual(HoverOverlay).Opacity = 0;
        UpdateLayoutMode();
        UpdateFeedback(false);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_observedDevice is not null)
        {
            _observedDevice.PropertyChanged -= OnDevicePropertyChanged;
            _observedDevice = null;
        }
    }

    private void OnDeviceChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (IsLoaded)
        {
            ObserveDevice();
            UpdateFeedback(false);
        }
    }

    private void ObserveDevice()
    {
        if (_observedDevice is not null)
        {
            _observedDevice.PropertyChanged -= OnDevicePropertyChanged;
        }

        _observedDevice = DataContext as DeviceRowViewModel;
        if (_observedDevice is not null)
        {
            _observedDevice.PropertyChanged += OnDevicePropertyChanged;
        }
    }

    private void OnDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DeviceRowViewModel.IsOn) or nameof(DeviceRowViewModel.CanToggle) or nameof(DeviceRowViewModel.IsRemoved))
        {
            UpdateFeedback(true);
            Grid.SetColumnSpan(OpenButton, _observedDevice?.HasPower == true ? 1 : 2);
        }
    }

    private void UpdateFeedback(bool animate)
    {
        double level = DataContext is DeviceRowViewModel { IsOn: true, IsOnline: true, IsRemoved: false, Power.HasValue: true } ? 1 : 0;
        ActiveHalo.SetLevel(level, animate);
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        SetHoverOpacity(1);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        SetHoverOpacity(0);
    }

    private void SetHoverOpacity(float opacity)
    {
        var visual = ElementCompositionPreview.GetElementVisual(HoverOverlay);
        visual.StopAnimation("Opacity");
        if (!AppMotion.IsEnabled) { visual.Opacity = opacity; return; }
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(120);
        animation.InsertKeyFrame(1, opacity);
        visual.StartAnimation("Opacity", animation);
    }
    private void UpdateLayoutMode()
    {
        if (Surface is null)
        {
            return;
        }

        Surface.Padding = new Thickness(0);
        Surface.MinHeight = IsCompact ? 64 : 96;
        Surface.Margin = IsCompact ? new Thickness(0, 0, 0, 8) : new Thickness(0);
        RoomLine.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath(nameof(DeviceRowViewModel.RoomStatusText)) });
        StateLine.Visibility = IsCompact ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumnSpan(OpenButton, _observedDevice?.HasPower == true ? 1 : 2);
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if (DataContext is DeviceRowViewModel device)
        {
            DeviceOpened?.Invoke(this, device);
        }
    }

    private void OnSurfaceTapped(object sender, TappedRoutedEventArgs e)
    {
        DependencyObject? source = e.OriginalSource as DependencyObject;
        while (source is not null && source != Surface)
        {
            if (source is ToggleSwitch or ButtonBase)
            {
                return;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        OnOpen(sender, e);
        e.Handled = true;
    }
}
