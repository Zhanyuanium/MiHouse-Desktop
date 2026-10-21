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
    private long _pollVersion;
    private JsonElement? _lastWrittenValue;
    private JsonElement? _lastKnownValue;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial bool IsRemoved { get; set; }

    [ObservableProperty]
    public partial bool IsSectionStart { get; set; }

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
    [NotifyPropertyChangedFor(nameof(DisplayCurrentValue))]
    public partial bool HasValue { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyPropertyChangedFor(nameof(HasDisplayValue))]
    public partial bool HasPendingChanges { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLongValue))]
    public partial string TextValue { get; set; }

    public bool HasLongValue => TextValue.Length > 120;

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
    public string Name => Property.DisplayName;
    public string ApplyLabel => $"应用：{Name}";
    public string ServiceName { get; }
    public bool IsAdvanced { get; internal set; }
    public bool IsDetailActive { get; internal set; }
    public IReadOnlyList<SpecValueList> Options => Property.Values ?? [];
    public bool IsBoolean => Property.Format == "bool";
    public bool IsChoice => !IsBoolean && IsInteger && Options.Count > 0;
    public bool IsRange => !IsBoolean && !IsChoice && IsNumeric && Property.Format is not "int64" and not "uint64" && Property.Range is { } range && range.Max > range.Min;
    public bool IsText => !IsBoolean && !IsChoice && !IsRange;
    public bool IsEditable => Property.Writable && (IsBoolean || IsNumeric || Property.Format == "string");
    public bool IsCommitText => IsEditable && IsText;
    public bool IsEditableChoice => IsChoice && IsEditable;
    public bool IsEditableBoolean => IsBoolean && IsEditable;
    public bool DisplayCurrentValue => HasValue && !IsChoice && !IsCommitText || HasValue && IsReadOnly;
    public bool IsReadOnly => !IsEditable;
    public string EditorText { get; set; } = string.Empty;
    public bool IsEditableRange => IsRange && IsEditable;
    public bool CanEdit => !IsRemoved && IsEditable && !IsBusy && !_token.IsCancellationRequested;
    public bool IsInteger => Property.Format.StartsWith("int", StringComparison.Ordinal) || Property.Format.StartsWith("uint", StringComparison.Ordinal);
    public bool IsNumeric => IsInteger || Property.Format is "float" or "double";
    public double Minimum => Property.Range?.Min ?? 0;
    public double Maximum => Property.Range?.Max ?? 1;
    public double Step => Property.Range is { Step: > 0 } range ? range.Step : 1;
    public double SliderStepFrequency => (Maximum - Minimum) / Step <= 200 ? Step : 0;
    public string NumericText => NumericValue.ToString("0.###", CultureInfo.CurrentCulture);
    public bool HasDisplayValue => HasValue || HasPendingChanges;

    private bool CanApply() => CanEdit && HasDisplayValue && (!IsChoice || SelectedOption is not null);

    internal long PollVersion => _pollVersion;

    internal void DiscardPendingChanges()
    {
        if (IsBusy || !HasPendingChanges)
        {
            return;
        }

        _pollVersion++;
        if (_lastKnownValue is { } known)
        {
            ReadValue(known);
            return;
        }

        bool wasUpdating = _isUpdatingValue;
        _isUpdatingValue = true;
        try
        {
            BoolValue = false;
            NumericValue = Minimum;
            SelectedOption = null;
            TextValue = string.Empty;
            HasValue = false;
            HasPendingChanges = false;
        }
        finally
        {
            _isUpdatingValue = wasUpdating;
        }
    }

    internal void TryApplyPolledValue(JsonElement value, long version)
    {
        if (version != _pollVersion || HasPendingChanges || IsBusy ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
            (IsBoolean && value.ValueKind is not JsonValueKind.True and not JsonValueKind.False))
        {
            return;
        }

        if (IsWriteUnconfirmed && _lastWrittenValue is { } written && !JsonElement.DeepEquals(value, written))
        {
            return;
        }

        ReadValue(value);
        IsWriteUnconfirmed = false;
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (IsRemoved)
        {
            Error = "设备已不在当前家庭";
            return;
        }

        if (!CanApply())
        {
            return;
        }

        if (IsChoice && SelectedOption is null)
        {
            Error = "请选择一个值";
            return;
        }

        IsBusy = true;
        _pollVersion++;
        Error = null;
        try
        {
            double numericValue = NumericValue;
            if (Property.Range is { Step: > 0 } range)
            {
                double steps = Math.Round((numericValue - range.Min) / range.Step, MidpointRounding.AwayFromZero);
                numericValue = range.Min + steps * range.Step;
                numericValue = Math.Clamp(numericValue, range.Min, range.Max);
            }

            JsonElement value = IsText && Property.Format == "string" ? JsonSerializer.SerializeToElement(EditorText)
                : IsText && IsInteger ? JsonDocument.Parse(EditorText).RootElement.Clone()
                : IsText && IsNumeric ? JsonSerializer.SerializeToElement(double.Parse(EditorText, CultureInfo.InvariantCulture))
                : IsBoolean
                ? JsonSerializer.SerializeToElement(BoolValue)
                : IsChoice
                    ? JsonSerializer.SerializeToElement(SelectedOption!.Value)
                    : IsInteger
                        ? JsonSerializer.SerializeToElement((long)Math.Round(numericValue))
                        : JsonSerializer.SerializeToElement(numericValue);
            if (IsRemoved)
            {
                Error = "设备已不在当前家庭";
                return;
            }

            await _controller.WriteAsync(_device, Key, value, _token);
            _token.ThrowIfCancellationRequested();
            _lastWrittenValue = value.Clone();
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

            _lastKnownValue = value.Clone();
            EditorText = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
            OnPropertyChanged(nameof(EditorText));
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

    public async Task CommitTextAsync(string text)
    {
        if (!CanEdit || !IsText) { return; }
        EditorText = text;
        HasPendingChanges = true;
        if (IsNumeric)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
            { Error = "请输入有效数值"; return; }
            if (IsInteger)
            {
                bool valid = Property.Format switch
                {
                    "uint64" => ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
                    "uint32" => uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
                    "uint16" => ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
                    "uint8" => byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
                    "int32" => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
                    "int16" => short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
                    "int8" => sbyte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
                    _ => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                };
                if (!valid) { Error = "请输入有效整数"; return; }
            }
            if (Property.Range is { } range && (number < range.Min || number > range.Max)) { Error = "数值超出设备允许范围"; return; }
        }
        HasPendingChanges = true;
        if (ApplyCommand.CanExecute(null)) { await ApplyCommand.ExecuteAsync(null); }
    }
}
