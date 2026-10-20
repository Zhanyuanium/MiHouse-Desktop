using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiHouse.Core.Auth;
using MiHouse.Core.Devices;
using MiHouse.Core.Session;
using MiHouse.Core.Spec;

namespace MiHouse.App.ViewModels;

[Microsoft.UI.Xaml.Data.Bindable]
public sealed partial class DeviceRowViewModel(MiDevice device)
{
    public MiDevice Device { get; } = device;
    public string Name => string.IsNullOrWhiteSpace(Device.Name) ? Device.Model : Device.Name;
    public string Model => Device.Model;
    public string Monogram => System.Globalization.StringInfo.GetNextTextElement(Name);
    public string OnlineLabel => Device.IsOnline ? "在线" : "离线";
    public bool IsOnline => Device.IsOnline;
    public string AccessibleName => $"{Name}，{OnlineLabel}，{Model}";
}

[Microsoft.UI.Xaml.Data.Bindable]
public sealed partial class RoomGroup(string name, IEnumerable<DeviceRowViewModel> devices) : ObservableCollection<DeviceRowViewModel>(devices)
{
    public string Name { get; } = name;
}

[Microsoft.UI.Xaml.Data.Bindable]
public sealed partial class DevicesViewModel : ObservableObject
{
    private readonly IMiHomeCloudClient _cloud;
    private readonly ISpecCatalog _specs;
    private readonly ICredentialStore _credentials;
    private readonly MiHomeSession _session;
    private CancellationTokenSource? _listLifetime;
    private CancellationTokenSource? _selectionLifetime;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsDetailBusy { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial string? DetailError { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string? DetailMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial DeviceRowViewModel? SelectedDevice { get; set; }

    public DevicesViewModel(IMiHomeCloudClient cloud, ISpecCatalog specs, ICredentialStore credentials, MiHomeSession session)
    {
        _cloud = cloud;
        _specs = specs;
        _credentials = credentials;
        _session = session;
    }

    public event Action? SignedOut;
    public ObservableCollection<RoomGroup> Rooms { get; } = [];
    public ObservableCollection<PropertyRowViewModel> Properties { get; } = [];
    public string AccountLabel => $"小米账号 {_session.UserId}";
    public bool HasSelection => SelectedDevice is not null;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _listLifetime?.Cancel();
        _listLifetime?.Dispose();
        _listLifetime = new CancellationTokenSource();
        CancellationToken token = _listLifetime.Token;
        IsBusy = true;
        IsEmpty = false;
        Error = null;
        try
        {
            IReadOnlyList<MiDevice> devices = await _cloud.GetDevicesAsync(_session, token);
            token.ThrowIfCancellationRequested();
            SelectedDevice = null;
            Rooms.Clear();
            foreach (IGrouping<string, MiDevice> room in devices.GroupBy(device =>
                string.IsNullOrWhiteSpace(device.RoomName) ? "未分配房间" : device.RoomName).OrderBy(room => room.Key))
            {
                Rooms.Add(new RoomGroup(room.Key, room.OrderBy(device => device.Name).Select(device => new DeviceRowViewModel(device))));
            }

            IsEmpty = devices.Count == 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                Error = ex.Message;
            }
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsBusy = false;
            }
        }
    }

    [RelayCommand]
    private async Task ReloadPropertiesAsync() => await LoadPropertiesAsync(SelectedDevice);

    private async Task LoadPropertiesAsync(DeviceRowViewModel? row)
    {
        _selectionLifetime?.Cancel();
        _selectionLifetime?.Dispose();
        _selectionLifetime = new CancellationTokenSource();
        CancellationToken token = _selectionLifetime.Token;
        Properties.Clear();
        DetailError = null;
        DetailMessage = null;
        IsDetailBusy = row is not null;
        if (row is null)
        {
            return;
        }

        try
        {
            SpecInstance? spec = await _specs.GetForModelAsync(row.Model, token);
            token.ThrowIfCancellationRequested();
            if (spec is null)
            {
                DetailMessage = "此型号暂无 miot-spec 属性定义";
                return;
            }

            MiPropertyKey[] keys = spec.Services.SelectMany(service => service.Properties
                .Where(property => property.Readable)
                .Select(property => new MiPropertyKey(service.Iid, property.Iid))).ToArray();
            IReadOnlyList<MiPropertyValue> values = keys.Length == 0 ? [] :
                await _cloud.GetPropertiesAsync(_session, row.Device.Did, keys, token);
            token.ThrowIfCancellationRequested();
            foreach (SpecService service in spec.Services)
            {
                foreach (SpecProperty property in service.Properties.Where(property => property.Readable || property.Writable || property.Notifiable))
                {
                    MiPropertyKey key = new(service.Iid, property.Iid);
                    MiPropertyValue? current = values.Where(value => value.Key == key).Select(value => (MiPropertyValue?)value).FirstOrDefault();
                    Properties.Add(new PropertyRowViewModel(_cloud, _session, row.Device.Did, service.Description,
                        service.Iid, property, current?.Value, token));
                }
            }

            if (Properties.Count == 0)
            {
                DetailMessage = "此设备没有可显示的属性";
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                DetailError = ex.Message;
            }
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsDetailBusy = false;
            }
        }
    }

    [RelayCommand]
    private void CloseDetails() => SelectedDevice = null;

    [RelayCommand]
    private async Task SignOutAsync()
    {
        Stop();
        SelectedDevice = null;
        Error = null;
        try
        {
            await _credentials.ClearAsync(CancellationToken.None);
            SignedOut?.Invoke();
        }
        catch (Exception ex)
        {
            Error = $"退出登录失败：{ex.Message}";
        }
    }

    public void Stop()
    {
        _listLifetime?.Cancel();
        _listLifetime?.Dispose();
        _listLifetime = null;
        _selectionLifetime?.Cancel();
        _selectionLifetime?.Dispose();
        _selectionLifetime = null;
        IsBusy = false;
        IsDetailBusy = false;
    }

    partial void OnSelectedDeviceChanged(DeviceRowViewModel? value) => _ = LoadPropertiesAsync(value);
}
