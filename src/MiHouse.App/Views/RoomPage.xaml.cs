using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MiHouse.App.ViewModels;

namespace MiHouse.App.Views;

[Microsoft.UI.Xaml.Data.Bindable]
public sealed class DeviceTileRow(DeviceRowViewModel[] devices, int columns, bool last)
{
    public DeviceRowViewModel[] Devices { get; } = devices;
    public int Columns { get; } = columns;
    public Thickness BottomSpacing { get; } = new(0, 0, 0, last ? 0 : 12);
}

public sealed partial class RoomPage : Page
{
    private DevicesViewModel? _observedViewModel;
    private int _columns;
    private bool _rowsUpdateQueued;
    public RoomPage() => InitializeComponent();
    public event EventHandler<DeviceRowViewModel>? DeviceOpened;
    private void OnDeviceOpened(object? sender, DeviceRowViewModel device) => DeviceOpened?.Invoke(this, device);

    private void OnPageLoaded(object sender, RoutedEventArgs e) => ObserveDevices();
    private void OnContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (IsLoaded) { ObserveDevices(); }
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        if (_observedViewModel is not null)
        {
            _observedViewModel.VisibleDevices.CollectionChanged -= OnDevicesChanged;
            _observedViewModel = null;
        }
    }

    private void ObserveDevices()
    {
        if (_observedViewModel is not null)
        {
            _observedViewModel.VisibleDevices.CollectionChanged -= OnDevicesChanged;
        }

        _observedViewModel = DataContext as DevicesViewModel;
        if (_observedViewModel is not null)
        {
            _observedViewModel.VisibleDevices.CollectionChanged += OnDevicesChanged;
        }

        UpdateTileRows();
    }

    private void OnDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_rowsUpdateQueued) { return; }
        _rowsUpdateQueued = true;
        DispatcherQueue.TryEnqueue(() => { _rowsUpdateQueued = false; if (IsLoaded) { UpdateTileRows(); } });
    }
    private void OnTilesSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Columns != _columns) { UpdateTileRows(); }
    }

    private int Columns => Math.Clamp((int)((Math.Max(0, Layout.ContentWidth - 16) + 12) / 312), 1, 6);
    private void UpdateTileRows()
    {
        _columns = Columns;
        // Virtualize rows, not fixed-height cells: notices and scaled text can grow
        // naturally, while each realized row contains at most six shared device rows.
        var chunks = _observedViewModel?.VisibleDevices.Chunk(_columns).ToArray() ?? [];
        Layout.ItemsSource = chunks.Select((devices, index) => new DeviceTileRow(devices, _columns, index == chunks.Length - 1)).ToArray();
    }
}
