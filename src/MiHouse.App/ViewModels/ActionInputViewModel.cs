using System.Globalization;
using System.Numerics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using MiHouse.Core.Spec;

namespace MiHouse.App.ViewModels;

[Microsoft.UI.Xaml.Data.Bindable]
public sealed partial class ActionInputViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool BoolValue { get; set; }

    [ObservableProperty]
    public partial SpecValueList? SelectedOption { get; set; }

    [ObservableProperty]
    public partial double NumericValue { get; set; }

    [ObservableProperty]
    public partial string StringValue { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid), nameof(ValidationHint))]
    public partial string IntegerText { get; set; }

    public ActionInputViewModel(SpecProperty property)
    {
        Property = property;
        NumericValue = double.NaN;
        StringValue = string.Empty;
        IntegerText = string.Empty;
    }

    public SpecProperty Property { get; }
    public string Name => Property.Description;
    public bool IsBoolean => Property.Format == "bool";
    public bool IsInteger => Property.Format.StartsWith("int", StringComparison.Ordinal) || Property.Format.StartsWith("uint", StringComparison.Ordinal);
    public bool IsNumeric => IsInteger || Property.Format is "float" or "double";
    public bool Is64BitInteger => Property.Format.StartsWith("int64", StringComparison.Ordinal) || IsUnsigned64BitInteger;
    public bool IsUnsigned64BitInteger => Property.Format.StartsWith("uint64", StringComparison.Ordinal);
    public IReadOnlyList<SpecValueList> Options => Property.Values ?? [];
    public bool IsChoice => IsInteger && !Is64BitInteger && Options.Count > 0;
    public bool IsNumber => IsNumeric && !IsChoice && !Is64BitInteger;
    public bool IsString => Property.Format == "string";
    public double Minimum => Property.Range?.Min ?? double.MinValue;
    public double Maximum => Property.Range?.Max ?? double.MaxValue;
    public double Step => Property.Range is { Step: > 0 } range ? range.Step : 1;
    public bool IsValid => !Is64BitInteger || Validate64BitInteger(out _) is null;
    public string? ValidationHint
    {
        get
        {
            if (!Is64BitInteger)
            {
                return null;
            }

            string? error = Validate64BitInteger(out _);
            string? allowed = Options.Count > 0
                ? $"可选值：{string.Join("、", Options.Select(option => option.Value.ToString(CultureInfo.InvariantCulture)))}。"
                : null;
            return error is null ? allowed : allowed is null ? error : $"{error} {allowed}";
        }
    }

    public JsonElement SerializeValue()
    {
        // Keep 64-bit integers as text until serialization; double cannot preserve them.
        if (Is64BitInteger)
        {
            string? error = Validate64BitInteger(out BigInteger integer);
            if (error is not null)
            {
                throw new InvalidOperationException(ValidationHint);
            }

            return IsUnsigned64BitInteger
                ? JsonSerializer.SerializeToElement((ulong)integer)
                : JsonSerializer.SerializeToElement((long)integer);
        }

        if (IsBoolean)
        {
            return JsonSerializer.SerializeToElement(BoolValue);
        }

        if (IsString)
        {
            return JsonSerializer.SerializeToElement(StringValue);
        }

        if (!IsNumeric)
        {
            throw new InvalidOperationException($"{Name}：不支持的参数格式 {Property.Format}");
        }

        double number = IsChoice
            ? SelectedOption?.Value ?? throw new InvalidOperationException($"请选择{Name}")
            : NumericValue;
        if (!double.IsFinite(number) || number < Minimum || number > Maximum)
        {
            throw new InvalidOperationException($"请输入{Name}的有效数值");
        }

        if (!IsChoice && Property.Range is { Step: > 0 } range)
        {
            double steps = Math.Round((number - range.Min) / range.Step, MidpointRounding.AwayFromZero);
            number = Math.Clamp(range.Min + steps * range.Step, range.Min, range.Max);
        }

        if (IsInteger && number != Math.Truncate(number))
        {
            throw new InvalidOperationException($"{Name}必须为整数");
        }

        try
        {
            return Property.Format switch
            {
                "uint8" => JsonSerializer.SerializeToElement(checked((byte)number)),
                "int8" => JsonSerializer.SerializeToElement(checked((sbyte)number)),
                "uint16" => JsonSerializer.SerializeToElement(checked((ushort)number)),
                "int16" => JsonSerializer.SerializeToElement(checked((short)number)),
                "uint32" or "uint" => JsonSerializer.SerializeToElement(checked((uint)number)),
                "int32" or "int" => JsonSerializer.SerializeToElement(checked((int)number)),
                "float" when float.IsFinite((float)number) => JsonSerializer.SerializeToElement((float)number),
                "double" => JsonSerializer.SerializeToElement(number),
                "float" => throw new OverflowException(),
                _ => throw new InvalidOperationException($"{Name}：不支持的参数格式 {Property.Format}")
            };
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException($"{Name}超出 {Property.Format} 的取值范围");
        }
    }

    private string? Validate64BitInteger(out BigInteger integer)
    {
        integer = default;
        if (IsUnsigned64BitInteger && ulong.TryParse(IntegerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong unsigned))
        {
            integer = unsigned;
        }
        else if (!IsUnsigned64BitInteger && long.TryParse(IntegerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long signed))
        {
            integer = signed;
        }
        else
        {
            return IsUnsigned64BitInteger ? "请输入有效的 uint64 整数" : "请输入有效的 int64 整数";
        }

        if (Property.Range is { } range)
        {
            if (!double.IsFinite(range.Min) || !double.IsFinite(range.Max) || !double.IsFinite(range.Step) || range.Min > range.Max)
            {
                return "参数范围无效，无法执行";
            }

            var min = ConstraintNumber(range.Min);
            var max = ConstraintNumber(range.Max);
            if (integer * min.Denominator < min.Numerator || integer * max.Denominator > max.Numerator)
            {
                return $"数值超出范围（{range.Min.ToString("R", CultureInfo.InvariantCulture)}～{range.Max.ToString("R", CultureInfo.InvariantCulture)}）";
            }

            if (range.Step > 0)
            {
                var step = ConstraintNumber(range.Step);
                BigInteger offset = (integer * min.Denominator - min.Numerator) * step.Denominator;
                BigInteger divisor = min.Denominator * step.Numerator;
                if (offset % divisor != 0)
                {
                    return $"数值必须从 {range.Min.ToString("R", CultureInfo.InvariantCulture)} 起按步长 {range.Step.ToString("R", CultureInfo.InvariantCulture)} 递增";
                }
            }
        }

        BigInteger parsed = integer;
        return Options.Count > 0 && !Options.Any(option => parsed == option.Value) ? "不是允许的值。" : null;
    }

    private static (BigInteger Numerator, BigInteger Denominator) ConstraintNumber(double value)
    {
        // Scale the spec's round-trip decimal text, never the user's 64-bit integer.
        string text = value.ToString("R", CultureInfo.InvariantCulture);
        int exponentIndex = text.IndexOfAny(['E', 'e']);
        int exponent = exponentIndex < 0 ? 0 : int.Parse(text[(exponentIndex + 1)..], CultureInfo.InvariantCulture);
        string significand = exponentIndex < 0 ? text : text[..exponentIndex];
        int point = significand.IndexOf('.');
        int scale = (point < 0 ? 0 : significand.Length - point - 1) - exponent;
        BigInteger numerator = BigInteger.Parse(significand.Replace(".", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);
        return scale > 0
            ? (numerator, BigInteger.Pow(10, scale))
            : (numerator * BigInteger.Pow(10, -scale), BigInteger.One);
    }
}
