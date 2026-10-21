using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MiHouse.App.ViewModels;

public enum ShellPageKind
{
    Home,
    Scenes,
    Room,
    Activity,
    Settings
}

public sealed partial class DevicesViewModel
{
    private string? _activeClassName;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCurrentPageCommand))]
    [NotifyPropertyChangedFor(nameof(PageTitle), nameof(IsRoomEmpty), nameof(IsRoomLoading), nameof(IsRoomFailed), nameof(HasRefreshFailure), nameof(ShowRefreshAction))]
    public partial ShellPageKind CurrentPage { get; set; }
    public bool ShowRefreshAction => CurrentPage != ShellPageKind.Settings;
    private bool CanRefreshCurrentPage() => !IsRefreshBusy && ShowRefreshAction;

    [RelayCommand(CanExecute = nameof(CanRefreshCurrentPage))]
    private async Task RefreshCurrentPageAsync()
    {
        if (CurrentPage == ShellPageKind.Scenes)
        {
            if (ScenePanel.RefreshCommand.CanExecute(null)) { await ScenePanel.RefreshCommand.ExecuteAsync(null); }
        }
        else if (RefreshCommand.CanExecute(null)) { await RefreshCommand.ExecuteAsync(null); }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle), nameof(IsRoomEmpty))]
    public partial RoomGroup? ActiveRoom { get; set; }

    public ObservableCollection<DeviceRowViewModel> VisibleDevices { get; } = [];
    public ObservableCollection<DeviceClassGroupViewModel> DeviceClasses { get; } = [];
    public ObservableCollection<SensorReadingViewModel> SensorReadings { get; } = [];
    public bool HasSensorReadings => SensorReadings.Count > 0;
    public IEnumerable<SensorMetricGroup> MetricGroups => SensorReadings.GroupBy(reading => reading.Name is "温度" or "湿度" ? reading.Name : "空气质量")
        .OrderBy(group => group.Key == "温度" ? 0 : group.Key == "湿度" ? 1 : 2)
        .Select(group => new SensorMetricGroup(group.Key, group.ToArray()));

    // Each completed device publishes its readings without waiting for the whole sweep.
    private void PublishSensorReadings(DeviceRowViewModel row)
    {
        SensorReadingViewModel[] readings = row.SensorReadings.ToArray();
        var present = new HashSet<string>(readings.Select(reading => reading.Name), StringComparer.Ordinal);
        for (int index = SensorReadings.Count - 1; index >= 0; index--)
        {
            SensorReadingViewModel existing = SensorReadings[index];
            if (existing.SourceId == row.Device.Did && !present.Contains(existing.Name))
            {
                SensorReadings.RemoveAt(index);
            }
        }

        foreach (SensorReadingViewModel reading in readings)
        {
            int index = FindReadingIndex(row.Device.Did, reading.Name);
            if (index < 0)
            {
                SensorReadings.Add(reading);
            }
            else if (SensorReadings[index].ValueText != reading.ValueText ||
                SensorReadings[index].SourceLabel != reading.SourceLabel)
            {
                SensorReadings[index] = reading;
            }
        }

        OnPropertyChanged(nameof(HasSensorReadings));
        OnPropertyChanged(nameof(MetricGroups));
    }

    private void RemoveSensorReadings(string sourceId)
    {
        for (int index = SensorReadings.Count - 1; index >= 0; index--)
        {
            if (SensorReadings[index].SourceId == sourceId) { SensorReadings.RemoveAt(index); }
        }

        OnPropertyChanged(nameof(HasSensorReadings));
        OnPropertyChanged(nameof(MetricGroups));
    }

    private int FindReadingIndex(string sourceId, string name)
    {
        for (int index = 0; index < SensorReadings.Count; index++)
        {
            if (SensorReadings[index].SourceId == sourceId && SensorReadings[index].Name == name) { return index; }
        }

        return -1;
    }
    public bool IsRoomLoading => CurrentPage == ShellPageKind.Room && IsBusy;
    public bool IsRoomFailed => CurrentPage == ShellPageKind.Room && !IsBusy && Error is not null && VisibleDevices.Count == 0;
    public bool HasRefreshFailure => CurrentPage == ShellPageKind.Room && !IsBusy && Error is not null && VisibleDevices.Count > 0;
    public bool IsRoomEmpty => CurrentPage == ShellPageKind.Room && !IsBusy && Error is null && VisibleDevices.Count == 0;
    public string PageTitle => CurrentPage switch
    {
        ShellPageKind.Room => ActiveRoom?.Name ?? "房间",
        ShellPageKind.Activity => "活动",
        ShellPageKind.Settings => "设置",
        ShellPageKind.Scenes => "场景",
        _ => "首页"
    };

    [RelayCommand]
    private async Task NavigateScenesAsync()
    {
        SelectedDevice = null;
        CurrentPage = ShellPageKind.Scenes;
        await ScenePanel.LoadCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void NavigateHome()
    {
        _activeClassName = null;
        SelectedDevice = null;
        CurrentPage = ShellPageKind.Home;
    }

    [RelayCommand]
    private void NavigateSettings()
    {
        SelectedDevice = null;
        CurrentPage = ShellPageKind.Settings;
    }

    [RelayCommand]
    private void NavigateActivity()
    {
        SelectedDevice = null;
        CurrentPage = ShellPageKind.Activity;
    }

    [RelayCommand]
    private void NavigateRoom(RoomGroup room) => ShowRoom(room);

    internal void ShowRoom(RoomGroup room)
    {
        _activeClassName = null;
        SelectedDevice = null;
        RebindRoom(room);
    }

    private void RebindRoom(RoomGroup room)
    {
        ActiveRoom = room;
        // Same instances stay put: a refresh must not rebuild the tile host.
        SyncItems(VisibleDevices, room.ToArray());

        CurrentPage = ShellPageKind.Room;
        OnPropertyChanged(nameof(IsRoomEmpty));
        OnPropertyChanged(nameof(IsRoomFailed));
        OnPropertyChanged(nameof(HasRefreshFailure));
    }

    private void ClearDashboard()
    {
        VisibleDevices.Clear();
        foreach (DeviceClassGroupViewModel group in DeviceClasses) { group.Dispose(); }
        DeviceClasses.Clear();
        SensorReadings.Clear();
        OnPropertyChanged(nameof(HasSensorReadings));
        OnPropertyChanged(nameof(MetricGroups));
        OnPropertyChanged(nameof(IsRoomEmpty));
    }

    private void SetDeviceClasses(IEnumerable<DeviceRowViewModel> rows)
    {
        var previous = DeviceClasses.ToDictionary(group => group.Name);
        var next = new List<DeviceClassGroupViewModel>();
        foreach (IGrouping<string, DeviceRowViewModel> group in rows.GroupBy(row => row.ClassName)
            .OrderBy(group => group.Min(row => row.ClassOrder)).ThenBy(group => group.Key))
        {
            if (previous.Remove(group.Key, out DeviceClassGroupViewModel? existing))
            {
                SyncItems(existing, group.ToArray());
                next.Add(existing);
            }
            else { next.Add(new DeviceClassGroupViewModel(group.Key, group)); }
        }

        var stale = previous.Values.ToArray();
        SyncItems(DeviceClasses, next);
        foreach (DeviceClassGroupViewModel group in stale) { group.Dispose(); }
    }

    internal void ShowClass(DeviceClassGroupViewModel group)
    {
        SelectedDevice = null;
        _activeClassName = group.Name;
        RebindRoom(new RoomGroup($"类别 · {group.Name}", group.Where(device => !device.IsRemoved)));
    }
}

[Microsoft.UI.Xaml.Data.Bindable]
public sealed class SensorMetricGroup(string name, SensorReadingViewModel[] readings)
{
    public string Name { get; } = name;
    public SensorReadingViewModel[] Readings { get; } = readings;
}
