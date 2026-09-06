using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VyralBot.UI.Controls.ColorPicker;

/// <summary>
/// Compact HSV color picker matching VyralBot's dark theme: a draggable saturation/value
/// square, a hue strip, a live preview swatch (hex + R/G/B readout) and an editable hex field.
/// </summary>
/// <remarks>
/// - <see cref="SelectedColor"/> is a two-way bindable dependency property; external changes
///   (swatch clicks, hex box edits, built-in theme syncs) are mirrored into the visuals.
/// - <see cref="ColorChanged"/> is raised on EVERY interactive change — including each frame of
///   a continuous drag — so hosts can push live theme updates.
/// - <see cref="SetColor(Color, bool)"/> sets the color programmatically without the DP write
///   re-raising through the interactive path.
/// - Mouse capture is taken on press and released on up, so drags keep tracking even when the
///   pointer leaves the picker (e.g. inside a ScrollViewer).
/// - All chrome brushes resolve via DynamicResource, so the picker re-skins on theme switches.
/// </remarks>
public partial class ColorPicker : UserControl
{
    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor), typeof(Color), typeof(ColorPicker),
        new FrameworkPropertyMetadata(
            Color.FromRgb(0x14, 0xB8, 0xA6),
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSelectedColorChanged));

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    /// <summary>Raised on every interactive change (including continuous drags) with the new color.</summary>
    public event EventHandler<Color>? ColorChanged;

    private double _hue;        // 0..360
    private double _saturation; // 0..1
    private double _value;      // 0..1
    private byte _alpha = 0xFF; // preserved from programmatic sets; kept across interactive edits
    private bool _syncing;      // reentrancy guard: DP change -> visuals -> DP change loops
    private bool _updatingHexText; // suppresses HexBox.TextChanged while we write the text ourselves
    private string _lastValidHex = "#14B8A6";

    private SolidColorBrush? _previewBrush;

    private enum DragArea { None, SatVal, Hue }

    private DragArea _activeDrag = DragArea.None;

    public ColorPicker()
    {
        InitializeComponent();

        _previewBrush = new SolidColorBrush(Color.FromRgb(0x14, 0xB8, 0xA6)); // mutable, unfrozen
        PreviewBorder.Background = _previewBrush;

        HexBox.TextChanged += HexBox_TextChanged;
        HexBox.KeyDown += HexBox_KeyDown;
        HexBox.LostFocus += HexBox_LostFocus;

        Loaded += (_, _) => SyncFromColor(SelectedColor, raiseEvent: false);
        SizeChanged += (_, _) => UpdateVisuals(); // ActualWidth/Height become available or change
    }

    /// <summary>
    /// Sets the color programmatically. When <paramref name="raiseEvent"/> is true the
    /// <see cref="ColorChanged"/> event fires too; the DP change itself never re-enters the
    /// interactive path, so this is safe to call from ColorChanged handlers.
    /// </summary>
    public void SetColor(Color color, bool raiseEvent)
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            SetValue(SelectedColorProperty, color);
            ColorToHsv(color, out _hue, out _saturation, out _value);
            _alpha = color.A;
            UpdateVisuals();
            if (raiseEvent)
                ColorChanged?.Invoke(this, color);
        }
        finally
        {
            _syncing = false;
        }
    }

    // ---- DP / sync plumbing ----

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (ColorPicker)d;
        if (picker._syncing) return; // change originated inside the picker
        picker.SyncFromColor((Color)e.NewValue, raiseEvent: false);
    }

    /// <summary>Adopts an externally set color into HSV state + visuals. Optionally raises ColorChanged.</summary>
    private void SyncFromColor(Color color, bool raiseEvent)
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            ColorToHsv(color, out _hue, out _saturation, out _value);
            _alpha = color.A;
            UpdateVisuals();
            if (raiseEvent)
                ColorChanged?.Invoke(this, color);
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>Commits the current HSV state as the selected color — used by all interactive input.</summary>
    private void CommitInteractiveColor()
    {
        var color = HsvToColor(_hue, _saturation, _value, _alpha);
        _syncing = true;
        try
        {
            // SetCurrentValue flows through TwoWay bindings without replacing the binding itself.
            SetCurrentValue(SelectedColorProperty, color);
            UpdateVisuals();
            ColorChanged?.Invoke(this, color); // raised on every drag frame → live updates
        }
        finally
        {
            _syncing = false;
        }
    }

    private void UpdateVisuals()
    {
        var color = HsvToColor(_hue, _saturation, _value, _alpha);
        _lastValidHex = ToHex(color);

        // Pure-hue endpoint of the saturation square (stop 1 of the horizontal gradient).
        if (SatValFill.Fill is LinearGradientBrush fill && fill.GradientStops.Count > 1)
            fill.GradientStops[1].Color = HsvToColor(_hue, 1.0, 1.0, 0xFF);

        if (_previewBrush is { } preview)
            preview.Color = color;

        HexLabel.Text = _lastValidHex;
        RgbLabel.Text = $"R {color.R} \u00B7 G {color.G} \u00B7 B {color.B}";

        var svWidth = SatValArea.ActualWidth;
        var svHeight = SatValArea.ActualHeight;
        if (svWidth > 0 && svHeight > 0)
        {
            Canvas.SetLeft(SatValThumb, Clamp01(_saturation) * svWidth - SatValThumb.Width / 2);
            Canvas.SetTop(SatValThumb, (1 - Clamp01(_value)) * svHeight - SatValThumb.Height / 2);
        }

        var hueWidth = HueArea.ActualWidth;
        var hueHeight = HueArea.ActualHeight;
        if (hueWidth > 0 && hueHeight > 0)
        {
            Canvas.SetLeft(HueThumb, Clamp01(_hue / 360.0) * hueWidth - HueThumb.Width / 2);
            Canvas.SetTop(HueThumb, (hueHeight - HueThumb.Height) / 2);
        }

        // Never fight the user while they are typing in the hex field.
        if (!HexBox.IsFocused)
            HexBox.Text = _lastValidHex;
    }

    // ---- saturation/value square input ----

    private void SatValArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _activeDrag = DragArea.SatVal;
        SatValArea.CaptureMouse(); // keep receiving moves outside the picker / panel bounds
        UpdateSatValFromMouse(e.GetPosition(SatValArea));
        e.Handled = true;
    }

    private void SatValArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (_activeDrag != DragArea.SatVal || !SatValArea.IsMouseCaptured) return;
        UpdateSatValFromMouse(e.GetPosition(SatValArea));
    }

    private void SatValArea_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_activeDrag == DragArea.SatVal)
            EndDrag(SatValArea);
        e.Handled = true;
    }

    private void SatValArea_LostMouseCapture(object sender, MouseEventArgs e)
    {
        // Capture can be stolen (e.g. a popup opens) — never leave a stale drag active.
        if (_activeDrag == DragArea.SatVal && !SatValArea.IsMouseCaptured)
            _activeDrag = DragArea.None;
    }

    private void UpdateSatValFromMouse(Point position)
    {
        var width = SatValArea.ActualWidth;
        var height = SatValArea.ActualHeight;
        if (width <= 0 || height <= 0) return;

        _saturation = Clamp01(position.X / width);
        _value = 1 - Clamp01(position.Y / height);
        CommitInteractiveColor();
    }

    // ---- hue strip input ----

    private void HueArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _activeDrag = DragArea.Hue;
        HueArea.CaptureMouse();
        UpdateHueFromMouse(e.GetPosition(HueArea));
        e.Handled = true;
    }

    private void HueArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (_activeDrag != DragArea.Hue || !HueArea.IsMouseCaptured) return;
        UpdateHueFromMouse(e.GetPosition(HueArea));
    }

    private void HueArea_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_activeDrag == DragArea.Hue)
            EndDrag(HueArea);
        e.Handled = true;
    }

    private void HueArea_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_activeDrag == DragArea.Hue && !HueArea.IsMouseCaptured)
            _activeDrag = DragArea.None;
    }

    private void UpdateHueFromMouse(Point position)
    {
        var width = HueArea.ActualWidth;
        if (width <= 0) return;

        _hue = Clamp01(position.X / width) * 360.0;
        CommitInteractiveColor();
    }

    private void EndDrag(UIElement area)
    {
        if (area.IsMouseCaptured)
            area.ReleaseMouseCapture();
        _activeDrag = DragArea.None;
    }

    // ---- hex field ----

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingHexText || !HexBox.IsFocused) return; // only react to user typing

        if (TryParseHex(HexBox.Text, out var color))
        {
            ColorToHsv(color, out _hue, out _saturation, out _value);
            _alpha = color.A;
            CommitInteractiveColor(); // live-apply valid prefixes while typing
        }
        // Invalid intermediate text (e.g. "#1") → keep the last color; reverted on Enter/LostFocus.
    }

    private void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        NormalizeOrRevertHexBox();
        e.Handled = true;
    }

    private void HexBox_LostFocus(object sender, RoutedEventArgs e) => NormalizeOrRevertHexBox();

    /// <summary>Canonicalizes a valid hex entry, or reverts garbage back to the last valid color.</summary>
    private void NormalizeOrRevertHexBox()
    {
        if (TryParseHex(HexBox.Text, out var color))
        {
            if (color != SelectedColor)
            {
                ColorToHsv(color, out _hue, out _saturation, out _value);
                _alpha = color.A;
                CommitInteractiveColor();
            }

            _updatingHexText = true;
            try
            {
                HexBox.Text = _lastValidHex; // canonical "#RRGGBB"
                HexBox.CaretIndex = HexBox.Text.Length;
            }
            finally
            {
                _updatingHexText = false;
            }
        }
        else
        {
            _updatingHexText = true;
            try
            {
                HexBox.Text = _lastValidHex; // garbage input → revert to the last valid color
                HexBox.CaretIndex = HexBox.Text.Length;
            }
            finally
            {
                _updatingHexText = false;
            }
        }
    }

    // ---- color math ----

    private static void ColorToHsv(Color c, out double hue, out double saturation, out double value)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        value = max;
        saturation = max <= 0 ? 0 : delta / max;

        if (delta <= 0)
        {
            hue = 0;
            return;
        }

        if (max == r)
            hue = 60.0 * (((g - b) / delta) % 6);
        else if (max == g)
            hue = 60.0 * (((b - r) / delta) + 2);
        else
            hue = 60.0 * (((r - g) / delta) + 4);

        if (hue < 0) hue += 360;
    }

    private static Color HsvToColor(double hue, double saturation, double value, byte alpha)
    {
        hue = ((hue % 360) + 360) % 360;
        double chroma = value * saturation;
        double x = chroma * (1 - Math.Abs(hue / 60.0 % 2 - 1));
        double m = value - chroma;

        double r, g, b;
        switch (Math.Floor(hue / 60.0))
        {
            case 0: r = chroma; g = x; b = 0; break;
            case 1: r = x; g = chroma; b = 0; break;
            case 2: r = 0; g = chroma; b = x; break;
            case 3: r = 0; g = x; b = chroma; break;
            case 4: r = x; g = 0; b = chroma; break;
            default: r = chroma; g = 0; b = x; break;
        }

        return Color.FromArgb(
            alpha,
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    /// <summary>Accepts "#RRGGBB", "RRGGBB", "#AARRGGBB", "AARRGGBB"; never throws.</summary>
    private static bool TryParseHex(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var s = text.Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length != 6 && s.Length != 8) return false;
        foreach (var ch in s)
        {
            if (!Uri.IsHexDigit(ch)) return false;
        }

        try
        {
            color = s.Length == 6
                ? Color.FromRgb(Convert.ToByte(s[..2], 16), Convert.ToByte(s[2..4], 16), Convert.ToByte(s[4..6], 16))
                : Color.FromArgb(Convert.ToByte(s[..2], 16), Convert.ToByte(s[2..4], 16), Convert.ToByte(s[4..6], 16), Convert.ToByte(s[6..8], 16));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
