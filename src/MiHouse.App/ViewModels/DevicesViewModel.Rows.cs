using System.ComponentModel;
using MiHouse.Core.Devices;

namespace MiHouse.App.ViewModels;

public sealed partial class DevicesViewModel
{
    private readonly Dictionary<string, DeviceRowViewModel> _deviceRows = new(StringComparer.Ordinal);
    private CancellationTokenSource? _homeLifetime;

    private DeviceRowViewModel[] MergeDevices(IReadOnlyList<MiDevice> devices)
    {
        _homeLifetime ??= new CancellationTokenSource();
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (MiDevice device in devices)
        {
            present.Add(device.Did);
            if (_deviceRows.TryGetValue(device.Did, out DeviceRowViewModel? row))
            {
                row.UpdateDevice(device);
            }
            else
            {
                row = new DeviceRowViewModel(device, _controller, _homeLifetime.Token);
                row.PropertyChanged += OnDeviceOperationChanged;
                _deviceRows.Add(device.Did, row);
            }
        }

        foreach (DeviceRowViewModel row in _deviceRows.Values.Where(row => !present.Contains(row.Device.Did)).ToArray())
        {
            row.IsRemoved = true;
            RemoveSensorReadings(row.Device.Did);
            if (!row.HasOperations)
            {
                DropDevice(row);
            }
        }

        return _deviceRows.Values.ToArray();
    }

    private void OnDeviceOperationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is DeviceRowViewModel device)
        {
            if (e.PropertyName == nameof(DeviceRowViewModel.IsRemoved) && ReferenceEquals(SelectedDevice, device))
            {
                DetailMessage = device.IsRemoved ? "设备已从当前家庭移除" : null;
            }
        }

        if (e.PropertyName == nameof(DeviceRowViewModel.HasOperations) && sender is DeviceRowViewModel row &&
            row.IsRemoved && !row.HasOperations && _deviceRows.TryGetValue(row.Device.Did, out DeviceRowViewModel? current) && ReferenceEquals(current, row))
        {
            DropDevice(row);
        }
    }

    private void DropDevice(DeviceRowViewModel row)
    {
        row.PropertyChanged -= OnDeviceOperationChanged;
        _deviceRows.Remove(row.Device.Did);
        if (ReferenceEquals(SelectedDevice, row))
        {
            SelectedDevice = null;
        }

        foreach (RoomGroup room in Rooms)
        {
            room.Remove(row);
        }

        foreach (DeviceClassGroupViewModel group in DeviceClasses.ToArray())
        {
            group.Remove(row);
            if (group.Count == 0)
            {
                group.Dispose();
                DeviceClasses.Remove(group);
            }
        }

        VisibleDevices.Remove(row);
        RemoveSensorReadings(row.Device.Did);
        row.StopOperations();
        IsEmpty = _deviceRows.Count == 0;
        OnPropertyChanged(nameof(IsRoomEmpty));
        OnPropertyChanged(nameof(IsRoomFailed));
        OnPropertyChanged(nameof(HasRefreshFailure));
    }

    private void ResetDeviceOperations()
    {
        foreach (DeviceRowViewModel row in _deviceRows.Values)
        {
            row.PropertyChanged -= OnDeviceOperationChanged;
            row.StopOperations();
        }

        _deviceRows.Clear();
        _homeLifetime?.Cancel();
        _homeLifetime?.Dispose();
        _homeLifetime = null;
    }
}
