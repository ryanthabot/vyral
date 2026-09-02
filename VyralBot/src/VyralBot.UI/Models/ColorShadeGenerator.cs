using System.Windows.Media;

namespace VyralBot.UI.Models;

/// <summary>Derives hover/pressed/light/dark shades from a base accent color (HSL math).</summary>
public static class ColorShadeGenerator
{
    public static Color Hover(Color baseColor) => ShiftLightness(baseColor, +0.10);
    public static Color Pressed(Color baseColor) => ShiftLightness(baseColor, -0.10);
    public static Color Light(Color baseColor) => ShiftLightness(baseColor, +0.30);
    public static Color Dark(Color baseColor) => ShiftLightness(baseColor, -0.25);

    public static bool TryParseHex(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var s = hex.Trim().TrimStart('#');
        try
        {
            if (s.Length == 6)
            {
                color = Color.FromRgb(Convert.ToByte(s[..2], 16), Convert.ToByte(s[2..4], 16), Convert.ToByte(s[4..6], 16));
                return true;
            }
            if (s.Length == 8)
            {
                color = Color.FromArgb(Convert.ToByte(s[..2], 16), Convert.ToByte(s[2..4], 16), Convert.ToByte(s[4..6], 16), Convert.ToByte(s[6..8], 16));
                return true;
            }
        }
        catch (FormatException)
        {
        }

        return false;
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color ShiftLightness(Color color, double delta)
    {
        RgbToHsl(color, out var h, out var s, out var l);
        l = Math.Clamp(l + delta, 0.0, 1.0);
        var rgb = HslToRgb(h, s, l);
        return Color.FromArgb(color.A, rgb[0], rgb[1], rgb[2]);
    }

    private static void RgbToHsl(Color c, out double h, out double s, out double l)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2.0;
        if (Math.Abs(max - min) < double.Epsilon)
        {
            h = s = 0;
            return;
        }

        double d = max - min;
        s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        h = max == r ? (g - b) / d + (g < b ? 6 : 0)
          : max == g ? (b - r) / d + 2
          : (r - g) / d + 4;
        h /= 6.0;
    }

    private static byte[] HslToRgb(double h, double s, double l)
    {
        if (Math.Abs(s) < double.Epsilon)
        {
            var gray = (byte)Math.Round(l * 255);
            return new[] { gray, gray, gray };
        }

        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        return new[]
        {
            (byte)Math.Round(HueToRgb(p, q, h + 1.0 / 3.0) * 255),
            (byte)Math.Round(HueToRgb(p, q, h) * 255),
            (byte)Math.Round(HueToRgb(p, q, h - 1.0 / 3.0) * 255),
        };
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        return t switch
        {
            < 1.0 / 6.0 => p + (q - p) * 6 * t,
            < 1.0 / 2.0 => q,
            < 2.0 / 3.0 => p + (q - p) * (2.0 / 3.0 - t) * 6,
            _ => p,
        };
    }
}
