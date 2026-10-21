using System.Text.Json;
using MiHouse.Core.Devices;

namespace MiHouse.App.ViewModels;

public sealed partial class DevicesViewModel
{
    private static readonly TimeSpan _listPollInterval = TimeSpan.FromSeconds(8);
    private const int _listPollConcurrency = 3;
    private CancellationTokenSource? _pollLifetime;
    private CancellationTokenSource? _listPollLifetime;
    private Task _listPollTask = Task.CompletedTask;
    private MiPropertyKey[] _readableKeys = [];
    private bool _hasLoadedValues;
    private bool _isPollingActive;

    internal void SetPollingActive(bool active)
    {
        _isPollingActive = active;
        if (active)
        {
            StartPolling();
            RestartListPolling();
        }
        else
        {
            StopPolling();
            StopListPolling();
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

    /// <summary>
    /// Starts the passive list poll: the tiles on the home and room pages follow the
    /// devices' physical state without the detail sheet being opened. A page, room or
    /// activation change cancels the running loop and starts a new one.
    /// </summary>
    internal void RestartListPolling()
    {
        Task previous = _listPollTask;
        StopListPolling();
        if (!_isPollingActive || CurrentPage is not (ShellPageKind.Home or ShellPageKind.Room))
        {
            return;
        }

        var lifetime = new CancellationTokenSource();
        _listPollLifetime = lifetime;
        _listPollTask = RunListPollAsync(previous, lifetime.Token);
    }

    private async Task RunListPollAsync(Task previous, CancellationToken token)
    {
        try
        {
            // Never overlap cycles across a restart: the old cycle finishes first.
            await previous.ConfigureAwait(true);
        }
        catch (Exception)
        {
            // The previous loop reports nothing; its token ending is the only stop signal.
        }

        await PollListAsync(token).ConfigureAwait(true);
    }

    private void StopListPolling()
    {
        _listPollLifetime?.Cancel();
        _listPollLifetime?.Dispose();
        _listPollLifetime = null;
    }

    private DeviceRowViewModel[] ListPollTargets()
    {
        IEnumerable<DeviceRowViewModel> rows = CurrentPage switch
        {
            ShellPageKind.Home => _deviceRows.Values,
            ShellPageKind.Room => VisibleDevices,
            _ => [],
        };

        // The detail sheet's open device has its own faster poll; writes own their rows.
        return rows.Where(row => !row.IsRemoved && row.IsOnline && !row.IsLoading && !row.HasOperations &&
            row.Power is not { IsBusy: true } && row.Power is not { HasPendingChanges: true } &&
            !ReferenceEquals(row, SelectedDevice)).ToArray();
    }

    private async Task PollListAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(_listPollInterval, token).ConfigureAwait(true);
                DeviceRowViewModel[] rows = ListPollTargets();
                if (rows.Length == 0)
                {
                    continue;
                }

                using var concurrency = new SemaphoreSlim(_listPollConcurrency);
                await Task.WhenAll(rows.Select(async row =>
                {
                    try
                    {
                        await concurrency.WaitAsync(token).ConfigureAwait(true);
                        try
                        {
                            // Recheck after waiting; the page or the row may have changed.
                            if (row.IsRemoved || !row.IsOnline || row.IsLoading || row.HasOperations ||
                                row.Power is { IsBusy: true } or { HasPendingChanges: true } ||
                                ReferenceEquals(row, SelectedDevice))
                            {
                                return;
                            }

                            // A passive read must not flash loading placeholders on the tiles.
                            await row.RefreshReadingsAsync(token, showLoading: false).ConfigureAwait(true);
                            token.ThrowIfCancellationRequested();
                            PublishSensorReadings(row);
                        }
                        finally
                        {
                            concurrency.Release();
                        }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                    }
                    catch (Exception)
                    {
                        // A failing device must not stop the others; the row shows its own error.
                    }
                })).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Page, room, activation or session change: this loop is done.
        }
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
