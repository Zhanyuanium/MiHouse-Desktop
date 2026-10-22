using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MiHouse.App.ViewModels;

public enum GroupPowerState { Off, On, Partial, Unknown }

[Microsoft.UI.Xaml.Data.Bindable]
public sealed partial class DeviceClassGroupViewModel : ObservableCollection<DeviceRowViewModel>, IDisposable
{
    private bool _isBusy;
    private string? _notice;
    public DeviceClassGroupViewModel(string name, IEnumerable<DeviceRowViewModel> devices)
    {
        Name = name;
        Card = new DeviceClassCardViewModel(this);
        foreach (DeviceRowViewModel device in devices) { Add(device); }
    }
    public string Name { get; }
    public DeviceClassCardViewModel Card { get; }
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; NotifyState(); } }
    public string? Notice { get => _notice; private set { _notice = value; NotifyState(); } }
    public bool HasPower => this.Any(device => device.HasPower && !device.IsRemoved);
    public bool CanToggle => !IsBusy && this.Any(device => device.CanToggle);
    public GroupPowerState PowerState
    {
        get
        {
            var rows = this.Where(device => device.HasPower && !device.IsRemoved).ToArray();
            if (rows.Length == 0 || rows.Any(device => device.Power is not { HasValue: true })) { return GroupPowerState.Unknown; }
            int on = rows.Count(device => device.IsOn);
            return on == 0 ? GroupPowerState.Off : on == rows.Length ? GroupPowerState.On : GroupPowerState.Partial;
        }
    }
    public double HaloLevel => PowerState switch { GroupPowerState.On => 1, GroupPowerState.Partial => 0.5, _ => 0 };
    public string Summary => $"{Count} 台设备";
    protected override void InsertItem(int index, DeviceRowViewModel item)
    {
        base.InsertItem(index, item);
        item.PropertyChanged += OnDeviceChanged;
        NotifyState();
    }
    protected override void RemoveItem(int index)
    {
        this[index].PropertyChanged -= OnDeviceChanged;
        base.RemoveItem(index);
        NotifyState();
    }
    protected override void ClearItems()
    {
        foreach (DeviceRowViewModel device in this) { device.PropertyChanged -= OnDeviceChanged; }
        base.ClearItems();
        NotifyState();
    }
    private void OnDeviceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DeviceRowViewModel.IsOn) or nameof(DeviceRowViewModel.CanToggle) or nameof(DeviceRowViewModel.IsRemoved) or nameof(DeviceRowViewModel.HasPower)) { NotifyState(); }
    }
    private void NotifyState()
    {
        Card.NotifyState();
        foreach (string property in new[] { nameof(IsBusy), nameof(Notice), nameof(HasPower), nameof(CanToggle), nameof(PowerState), nameof(HaloLevel), nameof(Summary) })
        { OnPropertyChanged(new PropertyChangedEventArgs(property)); }
    }
    public async Task SetPowerAsync(bool desired)
    {
        if (!CanToggle) { return; }
        IsBusy = true;
        Notice = null;
        using var concurrency = new SemaphoreSlim(4);
        try
        {
            // Stay on the UI context: the shared rows own optimistic state and
            // removal gates. Bound the async I/O, not thread-pool work.
            int skipped = 0;
            string?[] results = await Task.WhenAll(this.ToArray().Select(async device =>
            {
                await concurrency.WaitAsync();
                try
                {
                    // Recheck after waiting; a refresh may remove a queued device.
                    if (device.IsRemoved || !device.CanToggle)
                    {
                        Interlocked.Increment(ref skipped);
                        return null;
                    }
                    if (device.IsOn == desired) { return null; }
                    await device.SetPowerAsync(desired);
                    return device.Power?.Error is { } error ? $"{device.Name}：{error}" : null;
                }
                catch (Exception ex) { return $"{device.Name}：{DeviceErrorText.Describe(ex)}"; }
                finally { concurrency.Release(); }
            }));
            var notices = results.OfType<string>().ToList();
            if (skipped > 0)
            {
                // The switch tooltip promises the skips are called out, and silence hid them.
                notices.Add($"{skipped} 台设备已跳过（离线或不可控）");
            }

            // Only real errors and skipped devices are announced; a successful or partial write
            // is silent.
            Notice = notices.Count > 0 ? string.Join("；", notices) : null;
        }
        finally { IsBusy = false; }
    }
    public void Dispose() { foreach (DeviceRowViewModel device in this) { device.PropertyChanged -= OnDeviceChanged; } }
}

[Microsoft.UI.Xaml.Data.Bindable]
public sealed class DeviceClassCardViewModel(DeviceClassGroupViewModel group) : ObservableObject
{
    public DeviceClassGroupViewModel Group { get; } = group;
    public string Name => Group.Name;
    public string IconKey => Group.FirstOrDefault(device => !device.IsRemoved)?.CategoryKey ?? "other";
    public string Summary => Group.Summary;
    public string? Notice => Group.Notice;
    public bool CanToggle => Group.CanToggle;
    public bool HasPower => Group.HasPower;
    public GroupPowerState PowerState => Group.PowerState;
    public double HaloLevel => Group.HaloLevel;
    internal void NotifyState()
    {
        foreach (string name in new[] { nameof(IconKey), nameof(Summary), nameof(Notice), nameof(CanToggle), nameof(HasPower), nameof(PowerState), nameof(HaloLevel) }) { OnPropertyChanged(name); }
    }
}
