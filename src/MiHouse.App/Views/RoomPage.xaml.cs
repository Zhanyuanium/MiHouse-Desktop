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
    private DeviceTileRow[] _rows = [];
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
        int columns = Columns;
        bool sameColumns = columns == _columns;
        _columns = columns;
        // Virtualize rows, not fixed-height cells: notices and scaled text can grow
        // naturally, while each realized row contains at most six shared device rows.
        DeviceRowViewModel[][] chunks = _observedViewModel?.VisibleDevices.Chunk(columns).ToArray() ?? [];
        if (sameColumns && RowsMatch(chunks))
        {
            return;
        }

        _rows = chunks.Select((devices, index) => new DeviceTileRow(devices, columns, index == chunks.Length - 1)).ToArray();
        Layout.ItemsSource = _rows;
    }

    // A refresh that only changes values must not replace the list and replay its transitions.
    private bool RowsMatch(DeviceRowViewModel[][] chunks)
    {
        if (_rows.Length != chunks.Length) { return false; }
        for (int index = 0; index < chunks.Length; index++)
        {
            if (_rows[index].Columns != _columns || !_rows[index].Devices.SequenceEqual(chunks[index])) { return false; }
        }

        return true;
    }
}
