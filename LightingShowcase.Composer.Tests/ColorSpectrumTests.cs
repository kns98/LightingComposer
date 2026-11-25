using LightingShowcase.Composer;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class ColorSpectrumTests
{
    [Theory]
    [InlineData(0.0, 0.0, 0.0, 0.0, 0.0, 0.0)]
    [InlineData(1.0, 1.0, 1.0, 0.0, 0.0, 1.0)]
    [InlineData(1.0, 0.0, 0.0, 0.0, 1.0, 1.0)]
    [InlineData(0.0, 1.0, 0.0, 120.0, 1.0, 1.0)]
    [InlineData(0.0, 0.0, 1.0, 240.0, 1.0, 1.0)]
    [InlineData(0.5, 0.5, 0.5, 0.0, 0.0, 0.5)]
    public void RgbToHsv_KnownValues(double r, double g, double b, double h, double s, double v)
    {
        (double actualH, double actualS, double actualV) = ColorSpectrumPicker.RgbToHsv(r, g, b);
        Assert.True(Math.Abs(actualH - h) < 1e-9);
        Assert.True(Math.Abs(actualS - s) < 1e-9);
        Assert.True(Math.Abs(actualV - v) < 1e-9);
    }

    [Theory]
    [InlineData(0.0, 1.0, 1.0, 1.0, 0.0, 0.0)]
    [InlineData(120.0, 1.0, 1.0, 0.0, 1.0, 0.0)]
    [InlineData(240.0, 1.0, 1.0, 0.0, 0.0, 1.0)]
    [InlineData(60.0, 1.0, 1.0, 1.0, 1.0, 0.0)]
    [InlineData(0.0, 0.0, 0.5, 0.5, 0.5, 0.5)]
    [InlineData(360.0, 1.0, 1.0, 1.0, 0.0, 0.0)]
    public void HsvToRgb_KnownValues(double h, double s, double v, double r, double g, double b)
    {
        (double actualR, double actualG, double actualB) = ColorSpectrumPicker.HsvToRgb(h, s, v);
        Assert.True(Math.Abs(actualR - r) < 1e-9);
        Assert.True(Math.Abs(actualG - g) < 1e-9);
        Assert.True(Math.Abs(actualB - b) < 1e-9);
    }

    [Theory]
    [InlineData(0.16, 0.42, 0.13)]
    [InlineData(0.85, 0.65, 0.20)]
    [InlineData(0.10, 0.25, 0.70)]
    [InlineData(0.90, 0.92, 0.95)]
    public void Hsv_RoundTrip(double r, double g, double b)
    {
        (double h, double s, double v) = ColorSpectrumPicker.RgbToHsv(r, g, b);
        (double rr, double gg, double bb) = ColorSpectrumPicker.HsvToRgb(h, s, v);
        Assert.True(Math.Abs(rr - r) < 1e-9);
        Assert.True(Math.Abs(gg - g) < 1e-9);
        Assert.True(Math.Abs(bb - b) < 1e-9);
    }
}
