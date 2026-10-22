using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiHouse.Core.Auth;
using MiHouse.Core.Devices;
using MiHouse.Core.Session;
using MiHouse.Core.Spec;

namespace MiHouse.App.ViewModels;

[Microsoft.UI.Xaml.Data.Bindable]
public sealed partial class HomeRowViewModel : ObservableObject
{
    private MiHome _home;

    public HomeRowViewModel(MiHome home)
    {
        _home = home;
    }

    public MiHome Home
    {
        get => _home;
        set
        {
            if (Equals(_home, value))
            {
                return;
            }

            _home = value;
            OnPropertyChanged(nameof(Home));
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(IsShared));
            OnPropertyChanged(nameof(AccessibleName));
        }
    }

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
    private readonly SnapshotStore _snapshots;
    private readonly DeviceClassification _classification;
    private readonly long? _userId;
    private CancellationTokenSource? _listLifetime;
    private CancellationTokenSource? _selectionLifetime;
    private bool _isUpdatingHomes;
    private bool _isSigningOut;
    private bool _homesVerified;

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
    [NotifyPropertyChangedFor(nameof(HasDetailStatus))]
    public partial string? DetailStaleMessage { get; set; }

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

    public DevicesViewModel(IMiHomeCloudClient cloud, IDeviceController controller, ICredentialStore credentials,
        MiHomeSession session, SnapshotStore snapshots, DeviceClassification classification)
    {
        _cloud = cloud;
        _controller = controller;
        _credentials = credentials;
        _session = session;
        _snapshots = snapshots;
        _classification = classification;
        _userId = long.TryParse(session.UserId, NumberStyles.None, CultureInfo.InvariantCulture, out long userId)
            ? userId
            : null;
        ScenePanel = new ScenesViewModel(cloud, session, RequestSceneConfirmationAsync, snapshots);
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
    private async Task RefreshAsync() => await LoadDevicesAsync(refreshHomes: true);

    private async Task LoadDevicesAsync(bool refreshHomes = false)
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
            // The first load primes homes and the selected home's devices; an explicit refresh
            // also reconciles homes so new, renamed or moved entries appear. A home switch only
            // reloads the devices, unless the homes were never verified from the network.
            bool restoredHomes = false;
            if (Homes.Count == 0)
            {
                restoredHomes = RestoreSnapshot();
            }

            if (SelectedHome is null || restoredHomes || refreshHomes || !_homesVerified)
            {
                // Initial selection belongs to this load, not a second device request.
                _isUpdatingHomes = true;
                IReadOnlyList<MiHome>? fetchedHomes = null;
                try
                {
                    fetchedHomes = await _cloud.GetHomesAsync(_session, token);
                    token.ThrowIfCancellationRequested();
                    _homesVerified = true;

                    if (Homes.Count == 0)
                    {
                        foreach (MiHome home in fetchedHomes)
                        {
                            Homes.Add(new HomeRowViewModel(home));
                        }
                    }
                    else
                    {
                        ReconcileHomes(fetchedHomes);
                    }

                    OnPropertyChanged(nameof(HasHomes));
                    if (SelectedHome is null)
                    {
                        SelectedHome = SelectFallbackHome();
                    }
                }
                finally
                {
                    _isUpdatingHomes = false;
                }

                // A successful refresh always replaces the stored list and selection.
                if (fetchedHomes is not null && _userId is { } userId)
                {
                    _snapshots.SaveHomes(userId, fetchedHomes, GetSelectedHomeId(SelectedHome));
                }
            }

            HomeRowViewModel? selectedHome = SelectedHome;
            if (selectedHome is null)
            {
                HasNoHomes = true;
                return;
            }

            // A home already in memory paints its live structure; otherwise its cached
            // structure paints before the request. Cached rows stay loading and unverified so
            // nothing is written from cache data.
            if (!restoredHomes && _userId is { } cachedUserId)
            {
                RestoreHomeDevices(cachedUserId, selectedHome);
            }

            IReadOnlyList<MiDevice> devices = await _cloud.GetDevicesAsync(_session, selectedHome.Home, token);
            token.ThrowIfCancellationRequested();
            MiDevice[] previousDevices = _deviceRows.Values.Select(row => row.Device).ToArray();
            DeviceClassificationResult classified = await _classification.EnrichAsync(
                _session, devices, previousDevices, token);
            devices = classified.Devices;
            token.ThrowIfCancellationRequested();
            DeviceRowViewModel[] rows = MergeDevices(devices);
            _verifiedHomeId = selectedHome.Home.Id;
            SyncRooms(selectedHome, rows);

            IsEmpty = rows.Length == 0;
            SetDeviceClasses(rows);

            if (_userId is { } cacheUserId)
            {
                _snapshots.SaveDevices(cacheUserId, selectedHome.Home, devices);
            }

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

    /// <summary>
    /// Publishes the structure from the last successful refresh so the dashboard has
    /// content before the network answers. Values are never restored from the cache.
    /// </summary>
    private bool RestoreSnapshot()
    {
        if (_userId is not { } userId ||
            !_snapshots.TryLoadHomes(userId, out IReadOnlyList<MiHome> homes, out long? selectedHomeId))
        {
            return false;
        }

        _isUpdatingHomes = true;
        try
        {
            foreach (MiHome home in homes)
            {
                Homes.Add(new HomeRowViewModel(home));
            }

            OnPropertyChanged(nameof(HasHomes));
            SelectedHome = (selectedHomeId is { } id
                ? Homes.FirstOrDefault(row => long.TryParse(row.Home.Id, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out long homeId) && homeId == id)
                : null)
                ?? SelectFallbackHome();
        }
        finally
        {
            _isUpdatingHomes = false;
        }

        if (SelectedHome is { } selected)
        {
            RestoreHomeDevices(userId, selected);
        }

        return true;
    }

    /// <summary>
    /// Publishes the structure of <paramref name="home"/> before its request: from the live
    /// rows when the session already holds a verified inventory for that home, otherwise from
    /// the cached snapshot. A verified live inventory is never reconciled against the disk
    /// snapshot, so a stale snapshot can neither hide live devices nor resurrect removed ones.
    /// Cached rows keep <see cref="DeviceRowViewModel.IsLoading"/> set and stay unverified, so
    /// cached data is never used for writes.
    /// </summary>
    private void RestoreHomeDevices(long userId, HomeRowViewModel home)
    {
        DeviceRowViewModel[] rows;
        if (_verifiedHomeId == home.Home.Id)
        {
            // The home is already in memory: rebuild the view from the live rows only.
            rows = _deviceRows.Values.Where(row => !row.IsRemoved).ToArray();
        }
        else if (_snapshots.TryLoadDevices(userId, home.Home, out IReadOnlyList<MiDevice> devices))
        {
            rows = MergeDevices(devices, isLoading: true, inventoryVerified: false)
                .Where(row => !row.IsRemoved)
                .ToArray();
        }
        else
        {
            return;
        }

        SyncRooms(home, rows);
        IsEmpty = rows.Length == 0;
        SetDeviceClasses(rows);
    }

    /// <summary>
    /// Updates the cached home rows in place from a successful refresh, keeping the
    /// instance the home switcher has selected.
    /// </summary>
    private void ReconcileHomes(IReadOnlyList<MiHome> homes)
    {
        var existing = new Dictionary<string, HomeRowViewModel>(StringComparer.Ordinal);
        foreach (HomeRowViewModel row in Homes)
        {
            existing[row.Home.Id] = row;
        }

        var next = new List<HomeRowViewModel>(homes.Count);
        foreach (MiHome home in homes)
        {
            if (existing.Remove(home.Id, out HomeRowViewModel? row))
            {
                row.Home = home;
            }
            else
            {
                row = new HomeRowViewModel(home);
            }

            next.Add(row);
        }

        HomeRowViewModel? selected = SelectedHome;
        bool keepSelection = selected is not null && next.Contains(selected);
        if (!keepSelection)
        {
            // Clear before the collection changes so the switcher cannot select twice.
            SelectedHome = null;
        }

        SyncItems(Homes, next);

        if (!keepSelection)
        {
            SelectedHome = SelectFallbackHome();
        }
    }

    private HomeRowViewModel? SelectFallbackHome() =>
        Homes.FirstOrDefault(home => home.Home.Rooms.Any(room => room.DeviceIds.Count > 0))
        ?? Homes.FirstOrDefault();

    private void PersistSelectedHome(HomeRowViewModel? home)
    {
        if (home is null || _userId is not { } userId)
        {
            return;
        }

        _snapshots.SaveHomes(userId, Homes.Select(row => row.Home).ToArray(), GetSelectedHomeId(home));
    }

    private static long? GetSelectedHomeId(HomeRowViewModel? home) =>
        long.TryParse(home?.Home.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) ? id : null;

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
            int definitionVersion = row.DefinitionVersion;
            await row.EnsureInitializedAsync().WaitAsync(token);
            token.ThrowIfCancellationRequested();
            if (definitionVersion != row.DefinitionVersion)
            {
                // A refresh replaced the control identity while initialization was in flight.
                // The reload started by that change owns the rows.
                return;
            }

            SpecInstance? spec = row.Spec ?? await _controller.GetSpecAsync(row.Device, token);
            token.ThrowIfCancellationRequested();
            if (definitionVersion != row.DefinitionVersion)
            {
                return;
            }

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
        // The credentials are the irreversible part. Tear nothing down until they are gone, so a
        // failed deletion leaves a fully working signed-in session.
        try
        {
            await _credentials.ClearAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Error = $"退出登录失败：{DeviceErrorText.Describe(ex)}";
            _isSigningOut = false;
            return;
        }

        Stop();
        SelectedDevice = null;
        Error = null;
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

    public void Stop()
    {
        _isPollingActive = false;
        _homesVerified = false;
        ResetPollingState();
        StopListPolling();
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
            PersistSelectedHome(value);
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
