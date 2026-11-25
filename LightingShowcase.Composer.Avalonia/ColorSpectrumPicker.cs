// A compact hue/saturation spectrum plus brightness slider for manual color
// picking. Emits standard Avalonia colors; hosts write the result into their
// existing hex field so all current validation and apply paths stay unchanged.
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace LightingShowcase.Composer;

internal sealed class ColorSpectrumPicker : Control
{
    public const int SpectrumWidth = 240;
    public const int SpectrumHeight = 160;
    private const int SliderWidth = 24;
    private const int Gap = 8;

    private static readonly Pen BlackPen = new(Brushes.Black, 3);
    private static readonly Pen WhitePen = new(Brushes.White, 1);

    private readonly WriteableBitmap spectrum;
    private readonly WriteableBitmap slider;
    private double hue;
    private double saturation = 1.0;
    private double colorValue = 1.0;

    public event Action<Color>? ColorPicked;

    public ColorSpectrumPicker()
    {
        Width = SpectrumWidth + Gap + SliderWidth;
        Height = SpectrumHeight;
        Focusable = true;
        spectrum = RenderSpectrum();
        slider = new WriteableBitmap(
            new PixelSize(SliderWidth, SpectrumHeight), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        RefreshSlider();
    }

    /// <summary>Gets or sets the selected color, updating the marker only (no event).</summary>
    public Color Color
    {
        get
        {
            (double r, double g, double b) = HsvToRgb(hue, saturation, colorValue);
            return Color.FromRgb((byte)(r * 255.0), (byte)(g * 255.0), (byte)(b * 255.0));
        }
        set
        {
            (hue, saturation, colorValue) = RgbToHsv(value.R / 255.0, value.G / 255.0, value.B / 255.0);
            RefreshSlider();
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        context.DrawImage(spectrum, new Rect(0, 0, SpectrumWidth, SpectrumHeight), new Rect(0, 0, SpectrumWidth, SpectrumHeight));
        context.DrawImage(slider, new Rect(0, 0, SliderWidth, SpectrumHeight), new Rect(SpectrumWidth + Gap, 0, SliderWidth, SpectrumHeight));

        double markerX = hue / 360.0 * SpectrumWidth;
        double markerY = (1.0 - saturation) * SpectrumHeight;
        context.DrawLine(BlackPen, new Point(0, markerY), new Point(SpectrumWidth, markerY));
        context.DrawLine(BlackPen, new Point(markerX, 0), new Point(markerX, SpectrumHeight));
        context.DrawLine(WhitePen, new Point(0, markerY), new Point(SpectrumWidth, markerY));
        context.DrawLine(WhitePen, new Point(markerX, 0), new Point(markerX, SpectrumHeight));

        double thumbY = (1.0 - colorValue) * SpectrumHeight;
        context.DrawLine(BlackPen,
            new Point(SpectrumWidth + Gap, thumbY),
            new Point(SpectrumWidth + Gap + SliderWidth, thumbY));
        context.DrawLine(WhitePen,
            new Point(SpectrumWidth + Gap, thumbY),
            new Point(SpectrumWidth + Gap + SliderWidth, thumbY));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            Pick(e.GetPosition(this));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            Pick(e.GetPosition(this));
    }

    private void Pick(Point point)
    {
        if (point.X >= 0 && point.X < SpectrumWidth && point.Y >= 0 && point.Y < SpectrumHeight)
        {
            hue = Math.Clamp(point.X / SpectrumWidth, 0.0, 1.0) * 360.0;
            saturation = Math.Clamp(1.0 - point.Y / SpectrumHeight, 0.0, 1.0);
            RefreshSlider();
        }
        else if (point.X >= SpectrumWidth + Gap && point.X < SpectrumWidth + Gap + SliderWidth &&
            point.Y >= 0 && point.Y < SpectrumHeight)
        {
            colorValue = Math.Clamp(1.0 - point.Y / SpectrumHeight, 0.0, 1.0);
        }
        else
        {
            return;
        }

        InvalidateVisual();
        ColorPicked?.Invoke(Color);
    }

    private void RefreshSlider()
    {
        byte[] pixels = new byte[SliderWidth * SpectrumHeight * 4];
        for (int y = 0; y < SpectrumHeight; y++)
        {
            double v = 1.0 - y / (double)(SpectrumHeight - 1);
            (double r, double g, double b) = HsvToRgb(hue, saturation, v);
            for (int x = 0; x < SliderWidth; x++)
            {
                int i = (y * SliderWidth + x) * 4;
                pixels[i] = (byte)(b * 255.0);
                pixels[i + 1] = (byte)(g * 255.0);
                pixels[i + 2] = (byte)(r * 255.0);
                pixels[i + 3] = 255;
            }
        }
        using ILockedFramebuffer frame = slider.Lock();
        Marshal.Copy(pixels, 0, frame.Address, pixels.Length);
    }

    private static WriteableBitmap RenderSpectrum()
    {
        WriteableBitmap bitmap = new(
            new PixelSize(SpectrumWidth, SpectrumHeight), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        byte[] pixels = new byte[SpectrumWidth * SpectrumHeight * 4];
        for (int y = 0; y < SpectrumHeight; y++)
        {
            double s = 1.0 - y / (double)(SpectrumHeight - 1);
            for (int x = 0; x < SpectrumWidth; x++)
            {
                double h = x / (double)(SpectrumWidth - 1) * 360.0;
                (double r, double g, double b) = HsvToRgb(h, s, 1.0);
                int i = (y * SpectrumWidth + x) * 4;
                pixels[i] = (byte)(b * 255.0);
                pixels[i + 1] = (byte)(g * 255.0);
                pixels[i + 2] = (byte)(r * 255.0);
                pixels[i + 3] = 255;
            }
        }
        using ILockedFramebuffer frame = bitmap.Lock();
        Marshal.Copy(pixels, 0, frame.Address, pixels.Length);
        return bitmap;
    }

    internal static (double R, double G, double B) HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360.0) + 360.0) % 360.0;
        s = Math.Clamp(s, 0.0, 1.0);
        v = Math.Clamp(v, 0.0, 1.0);
        double c = v * s;
        double x = c * (1.0 - Math.Abs((h / 60.0 % 2.0) - 1.0));
        double m = v - c;
        (double r, double g, double b) = (h / 60.0) switch
        {
            < 1.0 => (c, x, 0.0),
            < 2.0 => (x, c, 0.0),
            < 3.0 => (0.0, c, x),
            < 4.0 => (0.0, x, c),
            < 5.0 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };
        return (r + m, g + m, b + m);
    }

    internal static (double H, double S, double V) RgbToHsv(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        double h = 0.0;
        if (delta > 1e-9)
        {
            if (max == r)
                h = 60.0 * (((g - b) / delta % 6.0 + 6.0) % 6.0);
            else if (max == g)
                h = 60.0 * ((b - r) / delta + 2.0);
            else
                h = 60.0 * ((r - g) / delta + 4.0);
        }
        double s = max < 1e-9 ? 0.0 : delta / max;
        return (h, s, max);
    }
}
