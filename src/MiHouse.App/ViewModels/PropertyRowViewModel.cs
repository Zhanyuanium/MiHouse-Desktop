using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiHouse.Core.Devices;
using MiHouse.Core.Session;
using MiHouse.Core.Spec;

namespace MiHouse.App.ViewModels;

[Microsoft.UI.Xaml.Data.Bindable]
public sealed partial class PropertyRowViewModel : ObservableObject
{
    private readonly IMiHomeCloudClient _cloud;
    private readonly MiHomeSession _session;
    private readonly string _did;
    private readonly CancellationToken _token;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial bool BoolValue { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NumericText))]
    public partial double NumericValue { get; set; }

    [ObservableProperty]
    public partial SpecValueList? SelectedOption { get; set; }

    [ObservableProperty]
    public partial bool HasValue { get; set; }

    [ObservableProperty]
    public partial string TextValue { get; set; }

    public PropertyRowViewModel(IMiHomeCloudClient cloud, MiHomeSession session, string did,
        string serviceName, int siid, SpecProperty property, JsonElement? value, CancellationToken token)
    {
        _cloud = cloud;
        _session = session;
        _did = did;
        _token = token;
        TextValue = "值不可用";
        Property = property;
        ServiceName = serviceName;
        Key = new MiPropertyKey(siid, property.Iid);
        NumericValue = Minimum;
        if (value is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined })
        {
            ReadValue(value.Value);
        }
    }

    public SpecProperty Property { get; }
    public MiPropertyKey Key { get; }
    public string Name => Property.Description;
    public string ApplyLabel => $"应用：{Name}";
    public string ServiceName { get; }
    public IReadOnlyList<SpecValueList> Options => Property.Values ?? [];
    public bool IsBoolean => Property.Format == "bool";
    public bool IsChoice => !IsBoolean && IsInteger && Options.Count > 0;
    public bool IsRange => !IsBoolean && !IsChoice && IsNumeric && Property.Range is { } range && range.Max > range.Min;
    public bool IsText => !IsBoolean && !IsChoice && !IsRange;
    public bool IsEditable => Property.Writable && !IsText;
    public bool CanEdit => IsEditable && !IsBusy && !_token.IsCancellationRequested;
    public bool IsInteger => Property.Format.StartsWith("int", StringComparison.Ordinal) || Property.Format.StartsWith("uint", StringComparison.Ordinal);
    public bool IsNumeric => IsInteger || Property.Format is "float" or "double";
    public double Minimum => Property.Range?.Min ?? 0;
    public double Maximum => Property.Range?.Max ?? 1;
    public double Step => Property.Range is { Step: > 0 } range ? range.Step : 1;
    public string NumericText => NumericValue.ToString("0.###", CultureInfo.CurrentCulture);

    private bool CanApply() => CanEdit;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (IsChoice && SelectedOption is null)
        {
            Error = "请选择一个值";
            return;
        }

        IsBusy = true;
        Error = null;
        try
        {
            JsonElement value = IsBoolean
                ? JsonSerializer.SerializeToElement(BoolValue)
                : IsChoice
                    ? JsonSerializer.SerializeToElement(SelectedOption!.Value)
                    : IsInteger
                        ? JsonSerializer.SerializeToElement((long)Math.Round(NumericValue))
                        : JsonSerializer.SerializeToElement(NumericValue);
            await _cloud.SetPropertyAsync(_session, _did, Key, value, _token);
            _token.ThrowIfCancellationRequested();
            ReadValue(value);
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ReadValue(JsonElement value)
    {
        HasValue = true;
        TextValue = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            BoolValue = value.GetBoolean();
            TextValue = BoolValue ? "开启" : "关闭";
        }
        else if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
        {
            NumericValue = number;
            SelectedOption = Options.FirstOrDefault(option => option.Value == number);
            TextValue = SelectedOption?.Description ?? NumericText;
        }
    }
}
