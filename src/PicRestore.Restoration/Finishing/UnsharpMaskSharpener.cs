using PicRestore.Core.Abstractions;
using PicRestore.Core.Imaging;
using PicRestore.Core.Models;
using PicRestore.Restoration.Imaging;

namespace PicRestore.Restoration.Finishing;

/// <summary>
/// The protocol's "subtle sharpening" step: a luminance-only unsharp mask at
/// <see cref="ProcessingSettings.SharpeningAmount"/> (default 0.10 = 10%).
///
/// <list type="bullet">
/// <item><b>Luminance only</b> - the same brightness correction is added to R, G and B, so colours never
/// fringe or shift.</item>
/// <item><b>Radius scales with the scan</b> - a Gaussian sigma of about one "Polaroid grain" (long side /
/// 2500, clamped to 0.7-2.5 px), so a high-DPI scan and a phone snapshot of the same print get the same
/// visual amount.</item>
/// <item><b>Noise threshold</b> - differences smaller than ~1/255 are left alone, so flat skin and sky
/// don't get their grain amplified into "digital" texture.</item>
/// <item><b>Clamped and protected</b> - each pixel moves at most 4/255 at the default amount, and protected
/// ("never touch") pixels are skipped entirely.</item>
/// </list>
/// </summary>
public sealed class UnsharpMaskSharpener : ISharpener
{
    private const float NoiseThreshold = 1f / 255f;

    public RasterImage Sharpen(RasterImage image, DamageMask mask, ProcessingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(settings);

        float amount = Math.Clamp(settings.SharpeningAmount, 0f, 1f);
        if (amount <= 0f)
        {
            return image.Clone();
        }

        int width = image.Width;
        int height = image.Height;
        int n = width * height;
        float[] px = image.Pixels;

        var luminance = new float[n];
        for (int i = 0; i < n; i++)
        {
            luminance[i] = (0.2126f * px[i * 4]) + (0.7152f * px[(i * 4) + 1]) + (0.0722f * px[(i * 4) + 2]);
        }

        double sigma = Math.Clamp(Math.Max(width, height) / 2500.0, 0.7, 2.5);
        float[] blurred = ImageOps.GaussianBlur(luminance, width, height, sigma);

        // Cap how far any pixel can move: 0.4 x amount of full scale (4/255 at 10%) - enough to restore
        // edge definition, never enough to draw new edges.
        float maxDelta = 0.4f * amount;

        var output = image.Clone();
        float[] dst = output.Pixels;
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width) + x;
                if (mask.Protected[i])
                {
                    continue;
                }

                float detail = luminance[i] - blurred[i];
                if (MathF.Abs(detail) < NoiseThreshold)
                {
                    continue;
                }

                float delta = Math.Clamp(detail * amount, -maxDelta, maxDelta);
                dst[i * 4] = Math.Clamp(px[i * 4] + delta, 0f, 1f);
                dst[(i * 4) + 1] = Math.Clamp(px[(i * 4) + 1] + delta, 0f, 1f);
                dst[(i * 4) + 2] = Math.Clamp(px[(i * 4) + 2] + delta, 0f, 1f);
            }
        });

        return output;
    }
}
