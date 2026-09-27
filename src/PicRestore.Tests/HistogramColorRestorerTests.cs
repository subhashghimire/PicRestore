using PicRestore.Core.Imaging;
using PicRestore.Core.Models;
using PicRestore.Restoration.Color;
using Xunit;

namespace PicRestore.Tests;

public class HistogramColorRestorerTests
{
    private static RasterImage Tinted(float r, float g, float b, int seed)
    {
        var image = new RasterImage(60, 60);
        var rng = new Random(seed);
        for (int y = 0; y < 60; y++)
        {
            for (int x = 0; x < 60; x++)
            {
                float n = (float)((rng.NextDouble() - 0.5) * 0.3);
                image.SetPixel(x, y, Math.Clamp(r + n, 0, 1), Math.Clamp(g + n, 0, 1), Math.Clamp(b + n, 0, 1));
            }
        }

        return image;
    }

    private static (double R, double G, double B) Means(RasterImage image)
    {
        double r = 0, g = 0, b = 0;
        int n = image.Width * image.Height;
        for (int i = 0; i < n; i++)
        {
            r += image.Pixels[i * 4];
            g += image.Pixels[(i * 4) + 1];
            b += image.Pixels[(i * 4) + 2];
        }

        return (r / n, g / n, b / n);
    }

    [Fact]
    public void Restore_IsANoOp_AtZeroStrength()
    {
        RasterImage image = Tinted(0.5f, 0.4f, 0.3f, 1);
        var settings = ProcessingSettings.CreateDefault();
        settings.ColorRestorationStrength = 0f;

        RasterImage result = new HistogramColorRestorer().Restore(image, new DamageMask(60, 60), settings);

        Assert.True(image.Pixels.SequenceEqual(result.Pixels));
    }

    [Fact]
    public void Restore_ReducesAStrongColourCast()
    {
        // A full-range (so no contrast stretch) gradient with a strong blue cast.
        var image = new RasterImage(100, 10);
        for (int y = 0; y < 10; y++)
        {
            for (int x = 0; x < 100; x++)
            {
                float v = x / 99f;
                image.SetPixel(x, y, 0.9f * v, 1.0f * v, Math.Min(1f, 1.35f * v));
            }
        }

        RasterImage result = new HistogramColorRestorer().Restore(image, new DamageMask(100, 10), ProcessingSettings.CreateDefault());

        (double r0, _, double b0) = Means(image);
        (double r1, _, double b1) = Means(result);
        Assert.True(b1 / r1 < b0 / r0);
    }

    [Fact]
    public void Restore_KeepsAMildWarmCast()
    {
        // A gentle, era-appropriate warmth (within 12% of neutral) must not be neutralised.
        RasterImage image = Tinted(0.56f, 0.52f, 0.49f, 3);
        RasterImage result = new HistogramColorRestorer().Restore(image, new DamageMask(60, 60), ProcessingSettings.CreateDefault());

        (double r0, _, double b0) = Means(image);
        (double r1, _, double b1) = Means(result);
        Assert.True(r1 / b1 >= r0 / b0 - 1e-3);
    }
}
