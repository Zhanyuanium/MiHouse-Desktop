using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using MiHouse.Core.Devices;
using MiHouse.Core.Spec;

namespace MiHouse.App.ViewModels;

[Microsoft.UI.Xaml.Data.Bindable]
public sealed partial class DeviceRowViewModel : ObservableObject
{
    private readonly IDeviceController _controller;
    private readonly CancellationTokenSource _operationLifetime;
    private readonly CancellationToken _token;
    private readonly Dictionary<MiPropertyKey, PropertyRowViewModel> _properties = [];
    private IReadOnlyList<ActionServiceViewModel>? _actions;
    private (SpecService Service, SpecProperty Property)[] _sensors = [];
    private MiDevice _device;
    private bool _syncing;
    private bool _stopped;
    private bool _hasLoadedValues;
    private Task? _initialization;

    public DeviceRowViewModel(MiDevice device, IDeviceController controller, CancellationToken homeToken, bool isLoading = false)
    {
        _device = device;
        _controller = controller;
        _operationLifetime = CancellationTokenSource.CreateLinkedTokenSource(homeToken);
        _token = _operationLifetime.Token;
        IsLoading = isLoading;
    }

    public MiDevice Device => _device;
    public string Name => string.IsNullOrWhiteSpace(Device.Name) ? Device.Model : Device.Name;
    public string Model => Device.Model;
    public string RoomName => string.IsNullOrWhiteSpace(Device.RoomName) ? "未分配房间" : Device.RoomName;
    public string Monogram => StringInfo.GetNextTextElement(Name);
    public bool IsOnline => Device.IsOnline;
    public string OnlineLabel => IsOnline ? "在线" : "离线";
    public string RoomStatusText => $"{RoomName} · {OnlineLabel}";
    public string QuickStatusText => string.IsNullOrEmpty(StateText) ? RoomStatusText : $"{RoomStatusText} · {StateText}";
    public string AccessibleName => $"{Name}，{RoomName}，{OnlineLabel}";
    public string PowerAccessibleName => Power is null && !IsLoading ? $"{Name}，{RoomName}，此设备没有统一的电源属性" : $"{Name}，{RoomName}";
    public string PowerHelpText => IsRemoved ? "设备已从当前家庭移除" : IsLoading ? "正在读取设备能力" : Power is null
        ? Error is null ? "此设备没有统一的电源属性" : "设备能力读取失败"
        : !Power.HasValue ? "当前值不可用，暂不能切换电源" : "切换电源，立即写入";
    private string? Category => DeviceClass.FromSpecType(Device.SpecType) ?? DeviceClass.FromSpecType(Spec?.Type);
    public string ClassName => DeviceClass.DisplayName(Category);
    internal int ClassOrder => DeviceClass.SortOrder(Category);
    internal SpecInstance? Spec { get; private set; }
    public ObservableCollection<SensorReadingViewModel> SensorReadings { get; } = [];
    public bool HasPower => Power is not null;
    public string TileStateText => IsLoading && !IsRemoved && !_hasLoadedValues ? string.Empty :
        Power is { HasValue: true, IsWriteUnconfirmed: false } && !IsRemoved ? string.Empty :
        !IsLoading && !string.IsNullOrEmpty(StateText) ? StateText :
        SensorReadings.OrderBy(reading => reading.Name == "温度" ? 0 : reading.Name == "湿度" ? 1 : 2)
            .Select(reading => $"{reading.Name} {reading.ValueText}").FirstOrDefault() ?? string.Empty;
    // The tile reserves the state line and shows this ring only until a value exists.
    public bool ShowsReadingPlaceholder => IsLoading && !IsRemoved && string.IsNullOrEmpty(TileStateText) &&
        Power is not { HasValue: true } && SensorReadings.Count == 0;
    internal CancellationToken OperationToken => _token;
    internal IEnumerable<PropertyRowViewModel> PropertyRows => _properties.Values;
    internal bool HasOperations => IsChanging || _properties.Values.Any(row => row.IsBusy) ||
        (_actions?.SelectMany(service => service.Actions).Any(row => row.IsBusy) ?? false);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggle), nameof(StateText), nameof(TileStateText), nameof(ShowsReadingPlaceholder), nameof(QuickStatusText), nameof(PowerHelpText), nameof(PowerAccessibleName))]
    public partial bool IsRemoved { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PowerAccessibleName))]
    [NotifyPropertyChangedFor(nameof(HasPower), nameof(TileStateText), nameof(ShowsReadingPlaceholder))]
    public partial PropertyRowViewModel? Power { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggle), nameof(StateText), nameof(TileStateText), nameof(ShowsReadingPlaceholder), nameof(QuickStatusText), nameof(PowerHelpText), nameof(PowerAccessibleName))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggle))]
    public partial bool IsChanging { get; set; }

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TileError), nameof(HasTileError))]
    public partial string? Error { get; set; }

    public string? TileError => Error ?? Power?.Error;
    public bool HasTileError => !string.IsNullOrEmpty(TileError);

    public bool CanToggle => IsOnline && !IsRemoved && !IsLoading && !IsChanging && !_token.IsCancellationRequested &&
        Power is { HasValue: true, IsBusy: false, HasPendingChanges: false };
    public string StateText => IsRemoved ? "设备已从当前家庭移除" :
        Power is { IsWriteUnconfirmed: true } ? "已写入，当前值未确认" :
        IsLoading ? "正在读取能力" : Power is null ? string.Empty : Power.HasValue ? Power.TextValue : "当前值不可用";

    internal Task EnsureInitializedAsync() => _initialization ??= InitializeAsync();

    internal void UpdateDevice(MiDevice device)
    {
        _device = device;
        IsRemoved = false;
        foreach (string name in new[] { nameof(Device), nameof(Name), nameof(Model), nameof(RoomName), nameof(Monogram),
            nameof(IsOnline), nameof(OnlineLabel), nameof(RoomStatusText), nameof(QuickStatusText), nameof(AccessibleName), nameof(PowerAccessibleName), nameof(ClassName), nameof(CanToggle) })
        {
            OnPropertyChanged(name);
        }
    }

    internal PropertyRowViewModel GetProperty(SpecService service, SpecProperty property)
    {
        var key = new MiPropertyKey(service.Iid, property.Iid);
        if (!_properties.TryGetValue(key, out PropertyRowViewModel? row))
        {
            row = new PropertyRowViewModel(_controller, Device, service.DisplayName, service.Iid, property, null, _token);
            row.IsAdvanced = SpecSections.Classify(service) == SpecSection.Advanced;
            row.IsRemoved = IsRemoved;
            row.PropertyChanged += OnOperationPropertyChanged;
            _properties.Add(key, row);
        }

        return row;
    }

    internal IReadOnlyList<ActionServiceViewModel> GetActions(SpecInstance spec,
        Func<ActionRowViewModel, CancellationToken, Task<bool>> confirm)
    {
        if (_actions is null)
        {
            // Unbinding is an account operation that this client does not expose.
            _actions = spec.Services.Select(service => new ActionServiceViewModel(service.DisplayName,
                service.Actions.Where(action => !action.Type.Contains("unbind", StringComparison.OrdinalIgnoreCase))
                     .Select(action => new ActionRowViewModel(_controller, Device, service.Iid, action, _token, confirm)
                     { ServiceName = service.DisplayName }).ToArray())
            { IsAdvanced = SpecSections.Classify(service) == SpecSection.Advanced })
                .Where(service => service.Actions.Count > 0).ToArray();
            foreach (ActionRowViewModel action in _actions.SelectMany(service => service.Actions))
            {
                action.IsRemoved = IsRemoved;
                action.PropertyChanged += OnOperationPropertyChanged;
            }
        }

        return _actions;
    }

    internal void DiscardDrafts()
    {
        foreach (PropertyRowViewModel row in _properties.Values)
        {
            row.DiscardPendingChanges();
        }
    }

    internal void StopOperations()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        _operationLifetime.Cancel();
        _operationLifetime.Dispose();
        OnPropertyChanged(nameof(CanToggle));
    }

    internal async Task RefreshReadingsAsync(CancellationToken token)
    {
        if (_initialization is null || !_initialization.IsCompleted || Spec is null)
        {
            if (_initialization is { IsCompleted: true } && Spec is null)
            {
                _initialization = null;
            }

            await EnsureInitializedAsync().WaitAsync(token);
        }

        // Definitions are ready (or initialization failed and set Error); only now read values.
        if (Spec is null)
        {
            return;
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _token);
        IsLoading = true;
        Error = null;
        try
        {
            await ReadSnapshotAsync(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!lifetime.IsCancellationRequested)
            {
                Error = DeviceErrorText.Describe(ex);
            }
        }
        finally
        {
            IsLoading = false;
            SyncPower();
        }
    }

    private async Task InitializeAsync()
    {
        IsLoading = true;
        Error = null;
        try
        {
            Spec = await _controller.GetSpecAsync(Device, _token);
            _token.ThrowIfCancellationRequested();
            OnPropertyChanged(nameof(ClassName));
            if (Spec is null)
            {
                return;
            }

            var properties = Spec.Services.SelectMany(service => service.Properties.Select(property => (Service: service, Property: property))).ToArray();
            var candidate = properties.Where(item => item.Property.Format == "bool" && item.Property.Writable)
                .FirstOrDefault(item => IsPowerType(item.Property.Type));
            if (candidate.Property is not null)
            {
                Power = GetProperty(candidate.Service, candidate.Property);
            }

            string identity = $"{Device.SpecType} {Model}";
            bool sensorLike = new[] { "sensor", "air-monitor", "air-purifier", "environment" }
                .Any(word => identity.Contains(word, StringComparison.OrdinalIgnoreCase));
            _sensors = properties.Where(item => sensorLike && item.Property.Readable &&
                (item.Property.Format.StartsWith("int", StringComparison.Ordinal) ||
                    item.Property.Format.StartsWith("uint", StringComparison.Ordinal) || item.Property.Format is "float" or "double") &&
                SensorLabel(item.Property.Type) is not null).ToArray();
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!_token.IsCancellationRequested)
            {
                Error = DeviceErrorText.Describe(ex);
            }
        }
        finally
        {
            IsLoading = false;
            SyncPower();
        }
    }

    private async Task ReadSnapshotAsync(CancellationToken token)
    {
        MiPropertyKey[] keys = _sensors.Select(item => new MiPropertyKey(item.Service.Iid, item.Property.Iid))
            .Concat(Power is { Property.Readable: true } ? [Power.Key] : Array.Empty<MiPropertyKey>()).Distinct().ToArray();
        if (keys.Length == 0)
        {
            _hasLoadedValues = true;
            OnPropertyChanged(nameof(TileStateText));
            OnPropertyChanged(nameof(ShowsReadingPlaceholder));
            return;
        }

        long? powerVersion = Power is { IsBusy: false, HasPendingChanges: false } ? Power.PollVersion : null;
        IReadOnlyDictionary<MiPropertyKey, JsonElement> values = await _controller.ReadAsync(Device, keys, token);
        token.ThrowIfCancellationRequested();
        if (Power is not null && powerVersion is { } version && values.TryGetValue(Power.Key, out JsonElement power))
        {
            Power.TryApplyPolledValue(power, version);
        }

        SensorReadings.Clear();
        foreach (var sensor in _sensors)
        {
            if (values.TryGetValue(new MiPropertyKey(sensor.Service.Iid, sensor.Property.Iid), out JsonElement value) &&
                value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) && double.IsFinite(number))
            {
                SensorReadings.Add(new SensorReadingViewModel(SensorLabel(sensor.Property.Type)!,
                    number.ToString("0.##", CultureInfo.CurrentCulture), $"{Name} · {RoomName} · {DateTime.Now:t}")
                { SourceId = Device.Did });
            }
        }

        _hasLoadedValues = true;
        OnPropertyChanged(nameof(TileStateText));
        OnPropertyChanged(nameof(ShowsReadingPlaceholder));
    }

    private static bool IsPowerType(string type)
    {
        // A versioned miot URN has a numeric suffix; match its semantic property stem only.
        string[] segments = type.Split(':');
        string semanticType = segments.Length > 4 && segments[2].Equals("property", StringComparison.OrdinalIgnoreCase)
            ? string.Join(':', segments.Take(4)) : type;
        return semanticType.EndsWith(":property:on", StringComparison.OrdinalIgnoreCase) ||
            semanticType.EndsWith(":property:power", StringComparison.OrdinalIgnoreCase);
    }

    private static string? SensorLabel(string type)
    {
        if (type.Contains(":temperature:", StringComparison.OrdinalIgnoreCase))
        {
            return "温度";
        }

        if (type.Contains(":relative-humidity:", StringComparison.OrdinalIgnoreCase) || type.Contains(":humidity:", StringComparison.OrdinalIgnoreCase))
        {
            return "湿度";
        }

        return new[] { ":pm2.5-density:", ":pm2-5-density:", ":pm25-density:" }
            .Any(name => type.Contains(name, StringComparison.OrdinalIgnoreCase)) ? "PM2.5" : null;
    }

    private void SyncPower()
    {
        _syncing = true;
        if (!IsChanging && Power is { HasPendingChanges: false })
        {
            IsOn = Power.BoolValue;
        }

        _syncing = false;
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(TileStateText));
        OnPropertyChanged(nameof(ShowsReadingPlaceholder));
        OnPropertyChanged(nameof(QuickStatusText));
        OnPropertyChanged(nameof(PowerHelpText));
    }

    private async Task ChangePowerAsync(bool desired)
    {
        if (IsRemoved)
        {
            Error = "设备已不在当前家庭";
            SyncPower();
            return;
        }

        PropertyRowViewModel? power = Power;
        if (!CanToggle || power is null)
        {
            SyncPower();
            return;
        }

        bool previous = power.BoolValue;
        IsChanging = true;
        Error = null;
        try
        {
            _token.ThrowIfCancellationRequested();
            if (IsRemoved)
            {
                Error = "设备已不在当前家庭";
                return;
            }

            power.BoolValue = desired;
            if (power.ApplyCommand.CanExecute(null))
            {
                await power.ApplyCommand.ExecuteAsync(null);
            }

            if (power.Error is not null)
            {
                power.BoolValue = previous;
                power.HasPendingChanges = false;
            }
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Error = DeviceErrorText.Describe(ex);
        }
        finally
        {
            IsChanging = false;
            SyncPower();
        }
    }

    internal Task SetPowerAsync(bool desired) => ChangePowerAsync(desired);

    partial void OnIsOnChanged(bool value)
    {
        if (!_syncing)
        {
            _ = ChangePowerAsync(value);
        }
    }

    partial void OnPowerChanged(PropertyRowViewModel? oldValue, PropertyRowViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnPowerPropertyChanged;
        }

        if (newValue is not null)
        {
            newValue.IsRemoved = IsRemoved;
            newValue.PropertyChanged += OnPowerPropertyChanged;
        }

        SyncPower();
    }

    private void OnPowerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SyncPower();
        OnPropertyChanged(nameof(TileError));
        OnPropertyChanged(nameof(HasTileError));
    }

    private void OnOperationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        string? operationError = sender switch
        {
            PropertyRowViewModel property => property.Error,
            ActionRowViewModel action => action.Error,
            _ => null
        };
        if (IsRemoved && e.PropertyName == nameof(Error) && operationError == "设备已不在当前家庭")
        {
            // Keep the blocked-request reason visible even if completion removes this row.
            Error = operationError;
        }

        if (e.PropertyName == nameof(PropertyRowViewModel.IsBusy))
        {
            OnPropertyChanged(nameof(HasOperations));
        }
    }

    partial void OnIsChangingChanged(bool value) => OnPropertyChanged(nameof(HasOperations));

    partial void OnIsRemovedChanged(bool value)
    {
        foreach (PropertyRowViewModel property in _properties.Values)
        {
            property.IsRemoved = value;
        }

        if (Power is not null)
        {
            Power.IsRemoved = value;
        }

        if (_actions is not null)
        {
            foreach (ActionRowViewModel action in _actions.SelectMany(service => service.Actions))
            {
                action.IsRemoved = value;
            }
        }
    }
}

[Microsoft.UI.Xaml.Data.Bindable]
public sealed partial class SensorReadingViewModel(string name, string valueText, string sourceLabel)
{
    public string Name { get; } = name;
    public string ValueText { get; } = valueText;
    public string MetricValueText => Name == "PM2.5" ? $"PM2.5 · {ValueText}" : ValueText;
    public string SourceLabel { get; } = sourceLabel;
    internal string SourceId { get; init; } = string.Empty;
}
