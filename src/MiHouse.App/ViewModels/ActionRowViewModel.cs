using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiHouse.Core.Devices;
using MiHouse.Core.Spec;

namespace MiHouse.App.ViewModels;

[Microsoft.UI.Xaml.Data.Bindable]
public sealed partial class ActionServiceViewModel(string name, IReadOnlyList<ActionRowViewModel> actions)
{
    public string Name { get; } = name;
    public bool IsAdvanced { get; set; }
    public IReadOnlyList<ActionRowViewModel> Actions { get; } = actions;
}

[Microsoft.UI.Xaml.Data.Bindable]
public sealed partial class ActionRowViewModel : ObservableObject
{
    private readonly IDeviceController _controller;
    private readonly MiDevice _device;
    private readonly CancellationToken _token;
    private readonly SpecAction _action;
    private readonly bool _isDestructive;
    private readonly Func<ActionRowViewModel, CancellationToken, Task<bool>> _confirm;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(InvokeCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit), nameof(CanInvoke))]
    [NotifyCanExecuteChangedFor(nameof(InvokeCommand))]
    public partial bool IsRemoved { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutput))]
    public partial string? Output { get; set; }

    public bool HasOutput => !string.IsNullOrWhiteSpace(Output);
    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    public ActionRowViewModel(IDeviceController controller, MiDevice device, int siid, SpecAction action,
        CancellationToken token, Func<ActionRowViewModel, CancellationToken, Task<bool>> confirm)
    {
        _controller = controller;
        _device = device;
        _token = token;
        _confirm = confirm;
        Key = new MiActionKey(siid, action.Iid);
        _action = action;
        _isDestructive = new[] { "reset", "factory", "delete", "remove" }
            .Any(keyword => action.Type.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        Inputs = action.Inputs.Select(property => new ActionInputViewModel(property)).ToArray();
        foreach (ActionInputViewModel input in Inputs)
        {
            input.PropertyChanged += OnInputPropertyChanged;
        }
    }

    public MiActionKey Key { get; }
    // SpecAction has no localized comment/display-name API. Keep its source label
    // as the explicit UI fallback; never invent translations from device identity.
    public string DisplayName => _action.Description;
    public string Name => DisplayName;
    public string ServiceName { get; internal set; } = string.Empty;
    public string ConfirmationMessage => $"服务：{ServiceName}\n确定要执行「{DisplayName}」吗？" +
        (_isDestructive ? "\n该操作可能重置设备或删除数据，且无法撤销。" : string.Empty);
    public IReadOnlyList<ActionInputViewModel> Inputs { get; }
    public bool HasInputs => Inputs.Count > 0;
    public string ButtonLabel => HasInputs ? "执行" : Name;
    public string AccessibleName => $"执行：{Name}";
    public bool CanEdit => !IsRemoved && !IsBusy && !_token.IsCancellationRequested;
    public bool CanInvoke => CanEdit && Inputs.All(input => input.IsValid);

    [RelayCommand(CanExecute = nameof(CanInvoke))]
    private async Task InvokeAsync()
    {
        if (IsRemoved)
        {
            Error = "设备已不在当前家庭";
            return;
        }

        if (!CanInvoke)
        {
            return;
        }

        IsBusy = true;
        Error = null;
        Output = null;
        try
        {
            JsonElement[] input = Inputs.Select(item => item.SerializeValue()).ToArray();
            bool confirmed = await _confirm(this, _token);
            if (IsRemoved)
            {
                Error = "设备已不在当前家庭";
                return;
            }

            if (!confirmed)
            {
                return;
            }

            _token.ThrowIfCancellationRequested();
            if (IsRemoved)
            {
                Error = "设备已不在当前家庭";
                return;
            }

            JsonElement result = await _controller.InvokeAsync(_device, Key, input, _token);
            _token.ThrowIfCancellationRequested();
            bool isEmpty = result.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => true,
                JsonValueKind.String => string.IsNullOrEmpty(result.GetString()),
                JsonValueKind.Array => result.GetArrayLength() == 0,
                JsonValueKind.Object => !result.EnumerateObject().Any(),
                _ => false
            };
            Output = isEmpty ? "已执行，无返回" : result.ToString();
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

    private void OnInputPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ActionInputViewModel.IsValid))
        {
            OnPropertyChanged(nameof(CanInvoke));
            InvokeCommand.NotifyCanExecuteChanged();
        }
    }
}
