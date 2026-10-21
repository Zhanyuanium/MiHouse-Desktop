using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiHouse.Core.Devices;
using MiHouse.Core.Session;

namespace MiHouse.App.ViewModels;

[Microsoft.UI.Xaml.Data.Bindable]
public sealed partial class ScenesViewModel : ObservableObject
{
    private readonly IMiHomeCloudClient _cloud;
    private readonly MiHomeSession _session;
    private readonly Func<SceneRowViewModel, CancellationToken, Task<bool>> _confirm;
    private readonly Dictionary<string, SceneRowViewModel> _rowsById = new(StringComparer.Ordinal);
    private MiHome? _home;
    private CancellationTokenSource? _homeLifetime;
    private CancellationTokenSource? _loadLifetime;
    private CancellationTokenSource? _noticeLifetime;
    private bool _isLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string? Notice { get; set; }

    public ScenesViewModel(IMiHomeCloudClient cloud, MiHomeSession session,
        Func<SceneRowViewModel, CancellationToken, Task<bool>> confirm)
    {
        _cloud = cloud;
        _session = session;
        _confirm = confirm;
    }

    public ObservableCollection<SceneRowViewModel> Scenes { get; } = [];
    public string HomeName => _home?.Name ?? "场景";
    public bool HasRunningScenes => _rowsById.Values.Any(row => row.IsBusy);
    public bool CanRefresh => _home is not null && !HasRunningScenes;
    public bool HasStatus => !string.IsNullOrWhiteSpace(Notice) || !string.IsNullOrWhiteSpace(Error);

    internal void SetHome(MiHome? home)
    {
        if (home is not null && _home?.Id == home.Id && _home.OwnerUserId == home.OwnerUserId && _homeLifetime is not null)
        {
            return;
        }

        _loadLifetime?.Cancel();
        _loadLifetime?.Dispose();
        _loadLifetime = null;
        _noticeLifetime?.Cancel();
        _noticeLifetime?.Dispose();
        _noticeLifetime = null;
        _homeLifetime?.Cancel();
        _homeLifetime?.Dispose();
        _homeLifetime = home is null ? null : new CancellationTokenSource();
        _home = home;
        _isLoaded = false;
        foreach (SceneRowViewModel row in _rowsById.Values)
        {
            row.PropertyChanged -= OnScenePropertyChanged;
        }

        _rowsById.Clear();
        Scenes.Clear();
        IsBusy = false;
        IsEmpty = false;
        Error = null;
        Notice = null;
        OnPropertyChanged(nameof(HomeName));
        NotifyRunStateChanged();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadAsync()
    {
        if (!_isLoaded && !IsBusy)
        {
            await FetchAsync();
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (CanRefresh)
        {
            await FetchAsync();
        }
    }

    private async Task FetchAsync()
    {
        MiHome? home = _home;
        if (home is null || _homeLifetime is null)
        {
            return;
        }

        _loadLifetime?.Cancel();
        _loadLifetime?.Dispose();
        _loadLifetime = CancellationTokenSource.CreateLinkedTokenSource(_homeLifetime.Token);
        CancellationToken token = _loadLifetime.Token;
        CancellationToken homeToken = _homeLifetime.Token;
        _isLoaded = false;
        IsBusy = true;
        IsEmpty = false;
        Error = null;
        try
        {
            IReadOnlyList<MiScene> scenes = await _cloud.GetScenesAsync(_session, home, token);
            token.ThrowIfCancellationRequested();
            var merged = new List<SceneRowViewModel>();
            foreach (MiScene scene in scenes.DistinctBy(scene => scene.Id))
            {
                if (_rowsById.TryGetValue(scene.Id, out SceneRowViewModel? row))
                {
                    row.UpdateScene(scene);
                }
                else
                {
                    row = new SceneRowViewModel(_cloud, _session, home, scene, homeToken, _confirm, ShowNotice);
                    row.PropertyChanged += OnScenePropertyChanged;
                    _rowsById.Add(scene.Id, row);
                }

                merged.Add(row);
            }

            for (int i = Scenes.Count - 1; i >= 0; i--)
            {
                if (!merged.Contains(Scenes[i]))
                {
                    Scenes.RemoveAt(i);
                }
            }

            for (int i = 0; i < merged.Count; i++)
            {
                int currentIndex = Scenes.IndexOf(merged[i]);
                if (currentIndex < 0)
                {
                    Scenes.Insert(i, merged[i]);
                }
                else if (currentIndex != i)
                {
                    Scenes.Move(currentIndex, i);
                }
            }

            // Retain hidden running rows by id until completion, even if a refresh removes them.
            foreach (SceneRowViewModel row in _rowsById.Values.Where(row => !row.IsBusy && !Scenes.Contains(row)).ToArray())
            {
                row.PropertyChanged -= OnScenePropertyChanged;
                _rowsById.Remove(row.Id);
            }

            NotifyRunStateChanged();
            _isLoaded = true;
            IsEmpty = Scenes.Count == 0;
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

    private void OnScenePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SceneRowViewModel.IsBusy) || sender is not SceneRowViewModel row ||
            !_rowsById.TryGetValue(row.Id, out SceneRowViewModel? current) || !ReferenceEquals(current, row))
        {
            return;
        }

        if (!row.IsBusy && !Scenes.Contains(row))
        {
            row.PropertyChanged -= OnScenePropertyChanged;
            _rowsById.Remove(row.Id);
        }

        NotifyRunStateChanged();
    }

    private void NotifyRunStateChanged()
    {
        OnPropertyChanged(nameof(HasRunningScenes));
        OnPropertyChanged(nameof(CanRefresh));
        RefreshCommand.NotifyCanExecuteChanged();
    }

    private void ShowNotice(string name)
    {
        if (_homeLifetime is null)
        {
            return;
        }

        _noticeLifetime?.Cancel();
        _noticeLifetime?.Dispose();
        _noticeLifetime = CancellationTokenSource.CreateLinkedTokenSource(_homeLifetime.Token);
        Notice = $"已执行「{name}」";
        _ = ClearNoticeAsync(_noticeLifetime.Token);
    }

    private async Task ClearNoticeAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), token);
            token.ThrowIfCancellationRequested();
            Notice = null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    internal void Stop() => SetHome(null);
}

[Microsoft.UI.Xaml.Data.Bindable]
public sealed partial class SceneRowViewModel : ObservableObject
{
    private readonly IMiHomeCloudClient _cloud;
    private readonly MiHomeSession _session;
    private readonly MiHome _home;
    private MiScene _scene;
    private readonly CancellationToken _token;
    private readonly Func<SceneRowViewModel, CancellationToken, Task<bool>> _confirm;
    private readonly Action<string> _onSuccess;
    private long _executionVersion;
    public long ExecutionVersion => _executionVersion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    public SceneRowViewModel(IMiHomeCloudClient cloud, MiHomeSession session, MiHome home, MiScene scene,
        CancellationToken token, Func<SceneRowViewModel, CancellationToken, Task<bool>> confirm, Action<string> onSuccess)
    {
        _cloud = cloud;
        _session = session;
        _home = home;
        _scene = scene;
        _token = token;
        _confirm = confirm;
        _onSuccess = onSuccess;
    }

    public string Name => _scene.Name;
    internal string Id => _scene.Id;
    public bool IsDisabled => !_scene.Enabled;
    public string AccessibleName => $"执行场景：{Name}";
    public bool CanRun => !IsBusy && !_token.IsCancellationRequested;

    internal void UpdateScene(MiScene scene)
    {
        _scene = scene;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsDisabled));
        OnPropertyChanged(nameof(AccessibleName));
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        if (!CanRun)
        {
            return;
        }

        IsBusy = true;
        Error = null;
        try
        {
            if (!await _confirm(this, _token))
            {
                return;
            }

            _token.ThrowIfCancellationRequested();
            _executionVersion++;
            OnPropertyChanged(nameof(ExecutionVersion));
            await _cloud.RunSceneAsync(_session, _home, _scene.Id, _token);
            _token.ThrowIfCancellationRequested();
            _onSuccess(Name);
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
            IsBusy = false;
        }
    }
}
