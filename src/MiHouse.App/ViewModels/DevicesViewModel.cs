using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiHouse.Core.Auth;
using MiHouse.Core.Devices;
using MiHouse.Core.Session;
using MiHouse.Core.Spec;

namespace MiHouse.App.ViewModels;

[Microsoft.UI.Xaml.Data.Bindable]
public sealed partial class HomeRowViewModel(MiHome home)
{
    public MiHome Home { get; } = home;
    public string Name => Home.Name;
    public bool IsShared => Home.IsShared;
    public string AccessibleName => IsShared ? $"{Name}，共享" : Name;
}

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
    private readonly IDeviceController _controller;
    private readonly ICredentialStore _credentials;
    private readonly MiHomeSession _session;
    private CancellationTokenSource? _listLifetime;
    private CancellationTokenSource? _selectionLifetime;
    private bool _isUpdatingHomes;
    private bool _isSigningOut;

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
    public partial bool HasNoHomes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderTitle))]
    [NotifyPropertyChangedFor(nameof(HasSelectedHome))]
    public partial HomeRowViewModel? SelectedHome { get; set; }

    [ObservableProperty]
    public partial string? DetailMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial DeviceRowViewModel? SelectedDevice { get; set; }

    public DevicesViewModel(IMiHomeCloudClient cloud, IDeviceController controller, ICredentialStore credentials, MiHomeSession session)
    {
        _cloud = cloud;
        _controller = controller;
        _credentials = credentials;
        _session = session;
        ScenePanel = new ScenesViewModel(cloud, session, RequestSceneConfirmationAsync);
    }

    public event Action? SignedOut;
    internal Func<ActionRowViewModel, CancellationToken, Task<bool>>? ConfirmAction { get; set; }
    internal Func<SceneRowViewModel, CancellationToken, Task<bool>>? ConfirmScene { get; set; }
    public ScenesViewModel ScenePanel { get; }
    public ObservableCollection<HomeRowViewModel> Homes { get; } = [];
    public ObservableCollection<RoomGroup> Rooms { get; } = [];
    public ObservableCollection<PropertyRowViewModel> Properties { get; } = [];
    public ObservableCollection<ActionServiceViewModel> ActionServices { get; } = [];
    public string AccountLabel => $"小米账号 {_session.UserId}";
    public string HeaderTitle => SelectedHome?.Name ?? "我的设备";
    public bool HasHomes => Homes.Count > 0;
    public bool HasSelection => SelectedDevice is not null;
    public bool HasSelectedHome => SelectedHome is not null;

    [RelayCommand]
    private async Task RefreshAsync() => await LoadDevicesAsync();

    private async Task LoadDevicesAsync()
    {
        if (_isSigningOut)
        {
            return;
        }

        _listLifetime?.Cancel();
        _listLifetime?.Dispose();
        _listLifetime = new CancellationTokenSource();
        CancellationToken token = _listLifetime.Token;
        IsBusy = true;
        IsEmpty = false;
        HasNoHomes = false;
        Error = null;
        SelectedDevice = null;
        Rooms.Clear();
        try
        {
            if (SelectedHome is null)
            {
                // Initial selection belongs to this load, not a second device request.
                _isUpdatingHomes = true;
                try
                {
                    if (Homes.Count == 0)
                    {
                        IReadOnlyList<MiHome> homes = await _cloud.GetHomesAsync(_session, token);
                        token.ThrowIfCancellationRequested();
                        foreach (MiHome home in homes)
                        {
                            Homes.Add(new HomeRowViewModel(home));
                        }

                        OnPropertyChanged(nameof(HasHomes));
                    }

                    SelectedHome = Homes.FirstOrDefault(home => home.Home.Rooms.Any(room => room.DeviceIds.Count > 0))
                        ?? Homes.FirstOrDefault();
                }
                finally
                {
                    _isUpdatingHomes = false;
                }
            }

            HomeRowViewModel? selectedHome = SelectedHome;
            if (selectedHome is null)
            {
                HasNoHomes = true;
                return;
            }

            IReadOnlyList<MiDevice> devices = await _cloud.GetDevicesAsync(_session, selectedHome.Home, token);
            token.ThrowIfCancellationRequested();
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
                Error = DeviceErrorText.Describe(ex);
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
        ResetPollingState();
        _selectionLifetime?.Cancel();
        _selectionLifetime?.Dispose();
        _selectionLifetime = new CancellationTokenSource();
        CancellationToken token = _selectionLifetime.Token;
        Properties.Clear();
        ActionServices.Clear();
        DetailError = null;
        DetailMessage = null;
        IsDetailBusy = row is not null;
        if (row is null)
        {
            return;
        }

        try
        {
            SpecInstance? spec = await _controller.GetSpecAsync(row.Device, token);
            token.ThrowIfCancellationRequested();
            if (spec is null)
            {
                DetailMessage = "此型号暂无 miot-spec 属性定义";
                return;
            }

            foreach (SpecService service in spec.Services.Where(service => service.Actions.Count > 0))
            {
                // Unbinding is an account operation that this client does not expose.
                SpecAction[] actions = service.Actions
                    .Where(action => !action.Type.Contains("unbind", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (actions.Length > 0)
                {
                    ActionServices.Add(new ActionServiceViewModel(service.Description, actions.Select(action =>
                        new ActionRowViewModel(_controller, row.Device, service.Iid, action, token, RequestActionConfirmationAsync)).ToArray()));
                }
            }

            _readableKeys = spec.Services.SelectMany(service => service.Properties
                .Where(property => property.Readable)
                .Select(property => new MiPropertyKey(service.Iid, property.Iid))).ToArray();
            foreach (SpecService service in spec.Services)
            {
                foreach (SpecProperty property in service.Properties.Where(property => property.Readable || property.Writable || property.Notifiable))
                {
                    Properties.Add(new PropertyRowViewModel(_controller, row.Device, service.Description,
                        service.Iid, property, null, token));
                }
            }

            await ReadCurrentValuesAsync(row.Device, _readableKeys, token);
            if (Properties.Count == 0 && ActionServices.Count == 0)
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
                DetailError = DeviceErrorText.Describe(ex);
            }
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsDetailBusy = false;
                StartPolling();
            }
        }
    }

    [RelayCommand]
    private void CloseDetails() => SelectedDevice = null;

    private Task<bool> RequestActionConfirmationAsync(ActionRowViewModel action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return ConfirmAction is { } confirm
            ? confirm(action, token)
            : throw new InvalidOperationException("无法显示确认对话框，操作未执行");
    }

    private Task<bool> RequestSceneConfirmationAsync(SceneRowViewModel scene, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return ConfirmScene is { } confirm
            ? confirm(scene, token)
            : throw new InvalidOperationException("无法显示确认对话框，操作未执行");
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        _isSigningOut = true;
        Stop();
        SelectedDevice = null;
        Error = null;
        try
        {
            await _credentials.ClearAsync(CancellationToken.None);
            _isUpdatingHomes = true;
            try
            {
                SelectedHome = null;
                Homes.Clear();
            }
            finally
            {
                _isUpdatingHomes = false;
            }

            OnPropertyChanged(nameof(HasHomes));
            Rooms.Clear();
            Properties.Clear();
            ActionServices.Clear();
            IsEmpty = false;
            HasNoHomes = false;
            DetailError = null;
            DetailMessage = null;
            SignedOut?.Invoke();
        }
        catch (Exception ex)
        {
            _isSigningOut = false;
            Error = $"退出登录失败：{DeviceErrorText.Describe(ex)}";
        }
    }

    public void Stop()
    {
        _isPollingActive = false;
        ResetPollingState();
        ScenePanel.Stop();
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

    partial void OnSelectedHomeChanged(HomeRowViewModel? value)
    {
        ScenePanel.SetHome(value?.Home);
        if (!_isUpdatingHomes)
        {
            _ = LoadDevicesAsync();
        }
    }
}
