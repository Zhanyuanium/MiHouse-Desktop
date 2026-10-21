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
    private readonly DeviceRowViewModel _owner;
    private readonly IDeviceController _controller;
    private readonly CancellationToken _token;
    private readonly SpecAction _action;
    private readonly bool _isDestructive;
    private readonly int _definitionVersion;
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

    public ActionRowViewModel(DeviceRowViewModel owner, int siid, SpecAction action,
        CancellationToken token, Func<ActionRowViewModel, CancellationToken, Task<bool>> confirm)
    {
        _owner = owner;
        _controller = owner.Controller;
        _token = token;
        _definitionVersion = owner.DefinitionVersion;
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
    public bool CanEdit => !IsRemoved && _owner.IsInventoryVerified && !IsBusy && !_token.IsCancellationRequested;
    public bool CanInvoke => CanEdit && !_owner.HasRunningAction && Inputs.All(input => input.IsValid);

    internal void NotifyInventoryStateChanged()
    {
        NotifyOwnerActionStateChanged();
    }

    internal void NotifyOwnerActionStateChanged()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanInvoke));
        InvokeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// True while this attempt still targets the address it was confirmed against. Rows stay
    /// valid after an address refresh, but the attempt must be repeated on the current one.
    /// </summary>
    private bool EnsureTargetCurrent(int targetVersion)
    {
        if (_owner.TargetVersion == targetVersion)
        {
            return true;
        }

        Error = "设备地址已更新，本次操作未执行。请重试。";
        return false;
    }

    /// <summary>
    /// True while this row still belongs to the owner's current definitions. A rebuild means the
    /// service/action identity changed and this row's key must not be sent to the new device.
    /// </summary>
    private bool EnsureDefinitionCurrent()
    {
        if (_owner.DefinitionVersion == _definitionVersion)
        {
            return true;
        }

        Error = "设备定义已更新，本次操作未执行。请重新打开设备详情后重试。";
        return false;
    }

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

        if (!EnsureDefinitionCurrent())
        {
            return;
        }

        if (!_owner.TryBeginAction())
        {
            Error = "已有操作正在执行，请等待完成后再试。";
            return;
        }

        int targetVersion = _owner.TargetVersion;
        IsBusy = true;
        Error = null;
        Output = null;
        try
        {
            JsonElement[] input = Inputs.Select(item => item.SerializeValue()).ToArray();
            bool confirmed = await _confirm(this, _token);
            if (!confirmed)
            {
                return;
            }

            if (IsRemoved)
            {
                Error = "设备已不在当前家庭";
                return;
            }

            // The dialog can wait arbitrarily long. If a refresh replaced the control identity,
            // the confirmed key and inputs belong to the old definitions; an address-only change
            // must be retried against the new endpoint.
            if (!EnsureDefinitionCurrent() || !EnsureTargetCurrent(targetVersion))
            {
                return;
            }

            _token.ThrowIfCancellationRequested();
            if (IsRemoved)
            {
                Error = "设备已不在当前家庭";
                return;
            }

            // Last check before the request leaves the client.
            if (!EnsureDefinitionCurrent() || !EnsureTargetCurrent(targetVersion))
            {
                return;
            }

            JsonElement result = await _controller.InvokeAsync(_owner.Device, Key, input, _token);
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
            _owner.EndAction();
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
