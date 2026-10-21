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
    // max(10 s, two 6 s detail poll cycles): after this the next successful read wins, even when
    // the device never reports the written value.
    private static readonly TimeSpan _writeConfirmationWindow = TimeSpan.FromSeconds(12);
    private readonly DeviceRowViewModel _owner;
    private readonly IDeviceController _controller;
    private readonly CancellationToken _token;
    private readonly int _definitionVersion;
    private bool _isUpdatingValue = true;
    private bool _hasEditorDraft;
    private long _pollVersion;
    private DateTimeOffset _writeProtectionDeadline;
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

    public PropertyRowViewModel(DeviceRowViewModel owner, string serviceName, int siid, SpecProperty property, JsonElement? value, CancellationToken token)
    {
        _owner = owner;
        _controller = owner.Controller;
        _token = token;
        _definitionVersion = owner.DefinitionVersion;
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
    public bool CanEdit => !IsRemoved && IsEditable && _owner.IsInventoryVerified && !IsBusy && !_token.IsCancellationRequested;
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
        bool wasDrafting = _hasEditorDraft;
        _hasEditorDraft = false;
        if (IsBusy || !HasPendingChanges)
        {
            if (!IsBusy && wasDrafting)
            {
                // Closing the details or leaving the row abandons the typed draft.
                if (_lastKnownValue is { } latest)
                {
                    ReadValue(latest);
                }
                else
                {
                    EditorText = string.Empty;
                    OnPropertyChanged(nameof(EditorText));
                }
            }

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
            EditorText = string.Empty;
            OnPropertyChanged(nameof(EditorText));
            HasValue = false;
            HasPendingChanges = false;
        }
        finally
        {
            _isUpdatingValue = wasUpdating;
        }
    }

    /// <summary>
    /// Records typed text so a poll cannot replace it. A value that already matches
    /// <see cref="EditorText"/> came from the binding, not from the user.
    /// </summary>
    internal void NoteEditorText(string text)
    {
        if (text == EditorText)
        {
            _hasEditorDraft = false;
            return;
        }

        _hasEditorDraft = true;
    }

    internal void NotifyInventoryStateChanged()
    {
        OnPropertyChanged(nameof(CanEdit));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    internal void TryApplyPolledValue(JsonElement value, long version)
    {
        if (version != _pollVersion || HasPendingChanges || IsBusy ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
            (IsBoolean && value.ValueKind is not JsonValueKind.True and not JsonValueKind.False))
        {
            return;
        }

        if (IsWriteUnconfirmed)
        {
            bool matchesWritten = _lastWrittenValue is { } written && JsonElement.DeepEquals(value, written);
            if (!matchesWritten && DateTimeOffset.UtcNow < _writeProtectionDeadline)
            {
                return;
            }

            // The written value arrived, or the bounded protection expired: the read is the truth.
            IsWriteUnconfirmed = false;
        }

        ReadValue(value, updateEditor: !_hasEditorDraft);
    }

    /// <summary>
    /// A successful read did not return this property. The old value may stay in the editor, but
    /// it is no longer the device's current value: clear the available flag so nothing writes
    /// from it until the device reports again.
    /// </summary>
    internal void MarkPolledValueMissing(long version)
    {
        if (version != _pollVersion || HasPendingChanges || IsBusy || !HasValue)
        {
            return;
        }

        bool wasUpdating = _isUpdatingValue;
        _isUpdatingValue = true;
        try
        {
            HasValue = false;
        }
        finally
        {
            _isUpdatingValue = wasUpdating;
        }
    }

    /// <summary>
    /// True while this row still belongs to the owner's current definitions. A rebuild means the
    /// service/property identity changed and this row's key must not be sent to the new device.
    /// </summary>
    private bool EnsureDefinitionCurrent()
    {
        if (_owner.DefinitionVersion == _definitionVersion)
        {
            return true;
        }

        Error = "设备定义已更新，本次修改未执行。请重新打开设备详情后重试。";
        return false;
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

        if (!EnsureDefinitionCurrent())
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

            JsonElement value;
            if (IsText && Property.Format == "string")
            {
                value = JsonSerializer.SerializeToElement(EditorText);
            }
            else if (IsText && IsInteger)
            {
                // User text is parsed with the declared width and sign, then written as a number;
                // raw text such as "+1" or "001" is not JSON.
                if (!TryParseIntegerText(EditorText, out value))
                {
                    Error = "请输入有效整数";
                    return;
                }
            }
            else if (IsText && IsNumeric)
            {
                value = JsonSerializer.SerializeToElement(double.Parse(EditorText, CultureInfo.InvariantCulture));
            }
            else
            {
                value = IsBoolean
                    ? JsonSerializer.SerializeToElement(BoolValue)
                    : IsChoice
                        ? JsonSerializer.SerializeToElement(SelectedOption!.Value)
                        : IsInteger
                            ? JsonSerializer.SerializeToElement((long)Math.Round(numericValue))
                            : JsonSerializer.SerializeToElement(numericValue);
            }
            if (IsRemoved)
            {
                Error = "设备已不在当前家庭";
                return;
            }

            // The confirmation-free write path still checks the definition twice: once when
            // the command starts and once immediately before the request leaves the client.
            if (!EnsureDefinitionCurrent())
            {
                return;
            }

            await _controller.WriteAsync(_owner.Device, Key, value, _token);
            _token.ThrowIfCancellationRequested();
            if (_owner.DefinitionVersion != _definitionVersion)
            {
                // The write went to the device snapshot captured by the controller. A definition
                // rebuild owns the new target now; do not verify the old key against it or claim
                // the write did not happen.
                return;
            }
            _lastWrittenValue = value.Clone();
            _writeProtectionDeadline = DateTimeOffset.UtcNow + _writeConfirmationWindow;
            ReadValue(value);
            // Without a readable value there is no poll that could overwrite the optimistic
            // state, so it needs no protection flag.
            if (!Property.Readable)
            {
                return;
            }

            IsWriteUnconfirmed = true;

            foreach (int delay in new[] { 300, 700, 1500 })
            {
                await Task.Delay(delay, _token);
                try
                {
                    IReadOnlyDictionary<MiPropertyKey, JsonElement> values = await _controller.ReadAsync(_owner.Device, [Key], _token);
                    _token.ThrowIfCancellationRequested();
                    if (values.TryGetValue(Key, out JsonElement current) &&
                        current.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined &&
                        JsonElement.DeepEquals(current, value))
                    {
                        ReadValue(current, updateEditor: !_hasEditorDraft);
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

    private void ReadValue(JsonElement value, bool updateEditor = true)
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
            if (updateEditor)
            {
                string text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
                if (EditorText != text)
                {
                    EditorText = text;
                    OnPropertyChanged(nameof(EditorText));
                }
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

    public async Task CommitTextAsync(string text)
    {
        if (!CanEdit || !IsText) { return; }
        // Keep the typed draft protected until the value is actually handed to the write.
        _hasEditorDraft = true;
        EditorText = text;
        HasPendingChanges = true;
        if (IsNumeric)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
            { Error = "请输入有效数值"; return; }
            if (IsInteger)
            {
                if (!TryParseIntegerText(text, out _)) { Error = "请输入有效整数"; return; }
            }
            if (Property.Range is { } range && (number < range.Min || number > range.Max)) { Error = "数值超出设备允许范围"; return; }
        }
        HasPendingChanges = true;
        if (ApplyCommand.CanExecute(null))
        {
            _hasEditorDraft = false;
            await ApplyCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Parses typed integer text with the property's declared width and sign and serializes it as
    /// a JSON number, so values such as <c>+1</c> or <c>001</c> never reach JSON as raw text.
    /// </summary>
    private bool TryParseIntegerText(string text, out JsonElement value)
    {
        value = default;
        switch (Property.Format)
        {
            case "uint64":
                if (!ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong u64))
                {
                    return false;
                }

                value = JsonSerializer.SerializeToElement(u64);
                return true;
            case "uint32":
                if (!uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint u32))
                {
                    return false;
                }

                value = JsonSerializer.SerializeToElement(u32);
                return true;
            case "uint16":
                if (!ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort u16))
                {
                    return false;
                }

                value = JsonSerializer.SerializeToElement(u16);
                return true;
            case "uint8":
                if (!byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte u8))
                {
                    return false;
                }

                value = JsonSerializer.SerializeToElement(u8);
                return true;
            case "int8":
                if (!sbyte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out sbyte i8))
                {
                    return false;
                }

                value = JsonSerializer.SerializeToElement(i8);
                return true;
            case "int16":
                if (!short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out short i16))
                {
                    return false;
                }

                value = JsonSerializer.SerializeToElement(i16);
                return true;
            case "int32":
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i32))
                {
                    return false;
                }

                value = JsonSerializer.SerializeToElement(i32);
                return true;
            default:
                if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long i64))
                {
                    return false;
                }

                value = JsonSerializer.SerializeToElement(i64);
                return true;
        }
    }
}
