using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiHouse.Core.Devices;
using MiHouse.Core.Spec;

namespace MiHouse.App.ViewModels;

[Microsoft.UI.Xaml.Data.Bindable]
public sealed partial class PropertyRowViewModel : ObservableObject
{
    private readonly IDeviceController _controller;
    private readonly MiDevice _device;
    private readonly CancellationToken _token;
    private bool _isUpdatingValue = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial bool IsWriteUnconfirmed { get; set; }

    [ObservableProperty]
    public partial bool BoolValue { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NumericText))]
    public partial double NumericValue { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial SpecValueList? SelectedOption { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyPropertyChangedFor(nameof(HasDisplayValue))]
    public partial bool HasValue { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyPropertyChangedFor(nameof(HasDisplayValue))]
    public partial bool HasPendingChanges { get; set; }

    [ObservableProperty]
    public partial string TextValue { get; set; }

    public PropertyRowViewModel(IDeviceController controller, MiDevice device,
        string serviceName, int siid, SpecProperty property, JsonElement? value, CancellationToken token)
    {
        _controller = controller;
        _device = device;
        _token = token;
        TextValue = string.Empty;
        Property = property;
        ServiceName = serviceName;
        Key = new MiPropertyKey(siid, property.Iid);
        NumericValue = Minimum;
        if (value is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined })
        {
            ReadValue(value.Value);
        }

        _isUpdatingValue = false;
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
    public bool IsEditableRange => IsRange && IsEditable;
    public bool CanEdit => IsEditable && !IsBusy && !_token.IsCancellationRequested;
    public bool IsInteger => Property.Format.StartsWith("int", StringComparison.Ordinal) || Property.Format.StartsWith("uint", StringComparison.Ordinal);
    public bool IsNumeric => IsInteger || Property.Format is "float" or "double";
    public double Minimum => Property.Range?.Min ?? 0;
    public double Maximum => Property.Range?.Max ?? 1;
    public double Step => Property.Range is { Step: > 0 } range ? range.Step : 1;
    public double SliderStepFrequency => (Maximum - Minimum) / Step <= 200 ? Step : 0;
    public string NumericText => NumericValue.ToString("0.###", CultureInfo.CurrentCulture);
    public bool HasDisplayValue => HasValue || HasPendingChanges;

    private bool CanApply() => CanEdit && HasDisplayValue && (!IsChoice || SelectedOption is not null);

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
        IsWriteUnconfirmed = false;
        try
        {
            double numericValue = NumericValue;
            if (Property.Range is { Step: > 0 } range)
            {
                double steps = Math.Round((numericValue - range.Min) / range.Step, MidpointRounding.AwayFromZero);
                numericValue = range.Min + steps * range.Step;
                numericValue = Math.Clamp(numericValue, range.Min, range.Max);
            }

            JsonElement value = IsBoolean
                ? JsonSerializer.SerializeToElement(BoolValue)
                : IsChoice
                    ? JsonSerializer.SerializeToElement(SelectedOption!.Value)
                    : IsInteger
                        ? JsonSerializer.SerializeToElement((long)Math.Round(numericValue))
                        : JsonSerializer.SerializeToElement(numericValue);
            await _controller.WriteAsync(_device, Key, value, _token);
            _token.ThrowIfCancellationRequested();
            ReadValue(value);
            IsWriteUnconfirmed = true;
            if (!Property.Readable)
            {
                return;
            }

            foreach (int delay in new[] { 300, 700, 1500 })
            {
                await Task.Delay(delay, _token);
                try
                {
                    IReadOnlyDictionary<MiPropertyKey, JsonElement> values = await _controller.ReadAsync(_device, [Key], _token);
                    _token.ThrowIfCancellationRequested();
                    if (values.TryGetValue(Key, out JsonElement current) &&
                        current.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined &&
                        JsonElement.DeepEquals(current, value))
                    {
                        ReadValue(current);
                        IsWriteUnconfirmed = false;
                        break;
                    }
                }
                catch (OperationCanceledException) when (_token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Keep the optimistic value and retry; a read failure is not a write failure.
                }
            }
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

    private void ReadValue(JsonElement value)
    {
        bool wasUpdating = _isUpdatingValue;
        _isUpdatingValue = true;
        try
        {
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

            HasValue = true;
            HasPendingChanges = false;
        }
        finally
        {
            _isUpdatingValue = wasUpdating;
        }
    }

    private void MarkEdited()
    {
        if (!_isUpdatingValue)
        {
            HasPendingChanges = true;
        }
    }

    partial void OnBoolValueChanged(bool value) => MarkEdited();
    partial void OnNumericValueChanged(double value) => MarkEdited();
    partial void OnSelectedOptionChanged(SpecValueList? value) => MarkEdited();
}
