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
public sealed partial class RoomGroup(string name, IEnumerable<DeviceRowViewModel> devices) : ObservableCollection<DeviceRowViewModel>(devices)
{
    private bool _isActive;
    public string Name { get; } = name;
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(IsActive)));
                OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(SelectionStatus)));
            }
        }
    }
    public string SelectionStatus => IsActive ? "当前房间" : string.Empty;
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
    [NotifyPropertyChangedFor(nameof(IsRoomLoading), nameof(IsRoomFailed), nameof(HasRefreshFailure), nameof(IsRoomEmpty), nameof(IsRefreshBusy))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCurrentPageCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsDetailBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRoomLoading), nameof(IsRoomFailed), nameof(HasRefreshFailure), nameof(IsRoomEmpty))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetailStatus))]
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
    [NotifyPropertyChangedFor(nameof(HasDetailStatus))]
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
        ScenePanel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ScenesViewModel.IsBusy))
            {
                OnPropertyChanged(nameof(IsRefreshBusy));
                RefreshCurrentPageCommand.NotifyCanExecuteChanged();
            }
        };
        Properties.CollectionChanged += OnDetailPropertiesChanged;
    }

    public event Action? SignedOut;
    internal Func<ActionRowViewModel, CancellationToken, Task<bool>>? ConfirmAction { get; set; }
    internal Func<SceneRowViewModel, CancellationToken, Task<bool>>? ConfirmScene { get; set; }
    public ScenesViewModel ScenePanel { get; }
    public bool IsRefreshBusy => IsBusy || ScenePanel.IsBusy;
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
        string? previousRoom = ActiveRoom?.Name;
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
            DeviceRowViewModel[] rows = MergeDevices(devices);
            SyncRooms(selectedHome, rows);

            IsEmpty = rows.Length == 0;
            SetDeviceClasses(rows);

            if (CurrentPage == ShellPageKind.Room)
            {
                if (_activeClassName is { } className && DeviceClasses.FirstOrDefault(group => group.Name == className) is { } activeClass)
                { ShowClass(activeClass); }
                else
                {
                    RoomGroup? room = Rooms.FirstOrDefault(room => room.Name == previousRoom);
                    if (room is not null)
                    {
                        RebindRoom(room);
                    }
                    else
                    {
                        NavigateHome();
                    }
                }
            }

            if (ScenePanel.RefreshCommand.CanExecute(null)) { _ = ScenePanel.RefreshCommand.ExecuteAsync(null); }

            // The list is published: stop the refresh glyph and let each tile fill in.
            IsBusy = false;
            using var concurrency = new SemaphoreSlim(4);
            await Task.WhenAll(rows.Select(async row =>
            {
                await concurrency.WaitAsync(token);
                try
                {
                    if (!row.IsRemoved)
                    {
                        await row.RefreshReadingsAsync(token);
                        token.ThrowIfCancellationRequested();
                        PublishSensorReadings(row);
                    }
                }
                finally
                {
                    concurrency.Release();
                }
            }));
            token.ThrowIfCancellationRequested();
            // Incremental: only classes whose membership changed are touched.
            SetDeviceClasses(_deviceRows.Values.ToArray());
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

    private void SyncRooms(HomeRowViewModel home, DeviceRowViewModel[] rows)
    {
        string[] names = home.Home.Rooms.Select(room => room.Name).Concat(rows.Select(row => row.RoomName))
            .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().OrderBy(name => name).ToArray();
        var existing = new Dictionary<string, RoomGroup>(StringComparer.Ordinal);
        foreach (RoomGroup room in Rooms) { existing[room.Name] = room; }

        // Reuse groups so a refresh updates tiles in place instead of replaying entry animations.
        for (int index = 0; index < names.Length; index++)
        {
            string name = names[index];
            DeviceRowViewModel[] members = rows.Where(row => row.RoomName == name).OrderBy(row => row.Name).ToArray();
            if (existing.TryGetValue(name, out RoomGroup? room))
            {
                SyncItems(room, members);
                int current = Rooms.IndexOf(room);
                if (current != index) { Rooms.Move(current, index); }
            }
            else
            {
                Rooms.Insert(index, new RoomGroup(name, members));
            }
        }

        while (Rooms.Count > names.Length) { Rooms.RemoveAt(Rooms.Count - 1); }
    }

    private static void SyncItems<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        var keep = new HashSet<T>(source);
        for (int index = target.Count - 1; index >= 0; index--)
        {
            if (!keep.Contains(target[index])) { target.RemoveAt(index); }
        }

        for (int index = 0; index < source.Count; index++)
        {
            T item = source[index];
            int current = target.IndexOf(item);
            if (current < 0) { target.Insert(index, item); }
            else if (current != index) { target.Move(current, index); }
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
        foreach (PropertyRowViewModel property in Properties) { property.IsDetailActive = false; }
        Properties.Clear();
        IsAdvancedExpanded = false;
        ActionServices.Clear();
        DetailError = null;
        DetailMessage = row is { IsRemoved: true } ? "设备已从当前家庭移除" : null;
        IsDetailBusy = row is not null;
        if (row is null)
        {
            return;
        }

        try
        {
            // Closing the sheet cancels the wait; the shared row keeps its own lifetime.
            await row.EnsureInitializedAsync().WaitAsync(token);
            token.ThrowIfCancellationRequested();
            SpecInstance? spec = row.Spec ?? await _controller.GetSpecAsync(row.Device, token);
            token.ThrowIfCancellationRequested();
            if (spec is null)
            {
                DetailMessage = "此型号暂无 miot-spec 属性定义";
                return;
            }

            foreach (ActionServiceViewModel actions in row.GetActions(spec, RequestActionConfirmationAsync))
            {
                ActionServices.Add(actions);
            }

            _readableKeys = spec.Services.SelectMany(service => service.Properties
                .Where(property => property.Readable)
                .Select(property => new MiPropertyKey(service.Iid, property.Iid))).ToArray();
            foreach (SpecService service in spec.Services)
            {
                foreach (SpecProperty property in service.Properties.Where(property => property.Readable || property.Writable || property.Notifiable))
                {
                    PropertyRowViewModel propertyRow = row.GetProperty(service, property);
                    propertyRow.IsDetailActive = true;
                    Properties.Add(propertyRow);
                }
            }

            // Structure first; values then fill the existing editors in place.
            RebuildDetailRows();
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
                if (row.IsRemoved)
                {
                    DetailMessage = "设备已从当前家庭移除";
                }

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
            ClearDashboard();
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
        ResetDeviceOperations();
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

    partial void OnSelectedDeviceChanging(DeviceRowViewModel? oldValue, DeviceRowViewModel? newValue) => oldValue?.DiscardDrafts();

    partial void OnSelectedHomeChanged(HomeRowViewModel? value)
    {
        if (!_isUpdatingHomes)
        {
            _listLifetime?.Cancel();
        }

        ScenePanel.SetHome(value?.Home);
        NavigateHome();
        ResetDeviceOperations();
        Rooms.Clear();
        ClearDashboard();
        if (!_isUpdatingHomes)
        {
            _ = LoadDevicesAsync();
        }
    }
}
