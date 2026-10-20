using System.Text.Json;
using MiHouse.Core.Devices;

namespace MiHouse.App.ViewModels;

public sealed partial class DevicesViewModel
{
    private CancellationTokenSource? _pollLifetime;
    private MiPropertyKey[] _readableKeys = [];
    private bool _hasLoadedValues;
    private bool _isPollingActive;

    internal void SetPollingActive(bool active)
    {
        _isPollingActive = active;
        if (active)
        {
            StartPolling();
        }
        else
        {
            StopPolling();
        }
    }

    private void ResetPollingState()
    {
        StopPolling();
        _readableKeys = [];
        _hasLoadedValues = false;
    }

    private void StartPolling()
    {
        StopPolling();
        if (!_isPollingActive || IsDetailBusy || SelectedDevice is null || _readableKeys.Length == 0 ||
            _selectionLifetime is null || _selectionLifetime.IsCancellationRequested)
        {
            return;
        }

        _pollLifetime = CancellationTokenSource.CreateLinkedTokenSource(_selectionLifetime.Token);
        _ = PollDeviceAsync(SelectedDevice.Device, _readableKeys, _pollLifetime.Token);
    }

    private void StopPolling()
    {
        _pollLifetime?.Cancel();
        _pollLifetime?.Dispose();
        _pollLifetime = null;
    }

    private async Task ReadCurrentValuesAsync(MiDevice device, MiPropertyKey[] keys, CancellationToken token)
    {
        var snapshots = Properties.Where(row => row.Property.Readable && !row.HasPendingChanges && !row.IsBusy)
            .Select(row => (Row: row, Version: row.PollVersion)).ToArray();
        IReadOnlyDictionary<MiPropertyKey, JsonElement> values = await _controller.ReadAsync(device, keys, token);
        token.ThrowIfCancellationRequested();
        foreach (var snapshot in snapshots)
        {
            if (values.TryGetValue(snapshot.Row.Key, out JsonElement value))
            {
                snapshot.Row.TryApplyPolledValue(value, snapshot.Version);
            }
        }

        _hasLoadedValues |= values.Any(pair => keys.Contains(pair.Key) &&
            pair.Value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined);
        if (_hasLoadedValues)
        {
            DetailError = null;
        }
    }

    private async Task PollDeviceAsync(MiDevice device, MiPropertyKey[] keys, CancellationToken token)
    {
        int failures = 0;
        TimeSpan nextInterval = TimeSpan.FromSeconds(6);
        try
        {
            while (true)
            {
                await Task.Delay(nextInterval, token);
                try
                {
                    await ReadCurrentValuesAsync(device, keys, token);
                    failures = 0;
                    nextInterval = TimeSpan.FromSeconds(6);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    token.ThrowIfCancellationRequested();
                    failures = Math.Min(failures + 1, 2);
                    nextInterval = TimeSpan.FromSeconds(failures == 1 ? 12 : 30);
                    if (!_hasLoadedValues)
                    {
                        DetailError = DeviceErrorText.Describe(ex);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }
}
