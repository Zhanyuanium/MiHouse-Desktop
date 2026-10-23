using Windows.UI;

namespace MiHouse.App.Appearance;

/// <summary>
/// The Windows-flavored accent ramp derived from a single base color: three lighter and three
/// darker steps used to repaint the <c>SystemAccentColor*</c> keys together.
/// </summary>
internal readonly record struct AccentPalette(
    Color Accent,
    Color Light1,
    Color Light2,
    Color Light3,
    Color Dark1,
    Color Dark2,
    Color Dark3)
{
    public static AccentPalette FromBase(Color accent)
    {
        (double hue, double saturation, double lightness) = ToHsl(accent);
        return new AccentPalette(
            accent,
            FromHsl(hue, Math.Max(0, saturation - 0.15), Math.Min(1, lightness + 0.10)),
            FromHsl(hue, Math.Max(0, saturation - 0.25), Math.Min(1, lightness + 0.22)),
            FromHsl(hue, Math.Max(0, saturation - 0.35), Math.Min(1, lightness + 0.34)),
            FromHsl(hue, Math.Min(1, saturation + 0.05), Math.Max(0, lightness - 0.10)),
            FromHsl(hue, Math.Min(1, saturation + 0.10), Math.Max(0, lightness - 0.22)),
            FromHsl(hue, Math.Min(1, saturation + 0.15), Math.Max(0, lightness - 0.34)));
    }

    private static (double Hue, double Saturation, double Lightness) ToHsl(Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double lightness = (max + min) / 2;
        if (max == min)
        {
            return (0, 0, lightness);
        }

        double delta = max - min;
        double saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);
        double hue;
        if (max == r)
        {
            hue = (g - b) / delta + (g < b ? 6 : 0);
        }
        else if (max == g)
        {
            hue = (b - r) / delta + 2;
        }
        else
        {
            hue = (r - g) / delta + 4;
        }

        return (hue / 6, saturation, lightness);
    }

    private static Color FromHsl(double hue, double saturation, double lightness)
    {
        if (saturation <= 0)
        {
            byte gray = ToByte(lightness);
            return Color.FromArgb(0xFF, gray, gray, gray);
        }

        double q = lightness < 0.5 ? lightness * (1 + saturation) : lightness + saturation - lightness * saturation;
        double p = 2 * lightness - q;
        return Color.FromArgb(
            0xFF,
            ToByte(HueToRgb(p, q, hue + 1.0 / 3)),
            ToByte(HueToRgb(p, q, hue)),
            ToByte(HueToRgb(p, q, hue - 1.0 / 3)));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0)
        {
            t += 1;
        }

        if (t > 1)
        {
            t -= 1;
        }

        if (t < 1.0 / 6)
        {
            return p + (q - p) * 6 * t;
        }

        if (t < 1.0 / 2)
        {
            return q;
        }

        if (t < 2.0 / 3)
        {
            return p + (q - p) * (2.0 / 3 - t) * 6;
        }

        return p;
    }

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
}
