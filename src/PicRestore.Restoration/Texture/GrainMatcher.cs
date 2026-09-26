using PicRestore.Core.Abstractions;
using PicRestore.Core.Imaging;
using PicRestore.Core.Models;

namespace PicRestore.Restoration.Texture;

/// <summary>
/// Measures the photo's own film-grain intensity from its intact regions (via a simple high-pass
/// residual) and re-synthesises matching noise onto reconstructed pixels, so a repair doesn't read as
/// suspiciously smooth next to the grain around it. See the design plan's "Texture, Grain &amp;
/// Photographic Authenticity" section.
/// </summary>
public sealed class GrainMatcher : IGrainMatcher
{
    public RasterImage Apply(RasterImage original, RasterImage reconstructed, DamageMask mask, ProcessingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(reconstructed);
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.MatchOriginalGrain)
        {
            return reconstructed;
        }

        float grainSigma = EstimateGrainSigma(original, mask, settings.RepairThreshold);
        if (grainSigma <= 0.0005f)
        {
            return reconstructed; // Source is essentially grain-free (or fully damaged) - nothing to match.
        }

        var rng = new Random(unchecked((original.Width * 397) ^ original.Height));
        var output = new RasterImage(reconstructed.Width, reconstructed.Height);

        for (int y = 0; y < reconstructed.Height; y++)
        {
            for (int x = 0; x < reconstructed.Width; x++)
            {
                (float r, float g, float b, float a) = reconstructed.GetPixel(x, y);

                if (mask.IsRepairable(x, y, settings.RepairThreshold))
                {
                    r = Math.Clamp(r + (float)(NextGaussian(rng) * grainSigma), 0f, 1f);
                    g = Math.Clamp(g + (float)(NextGaussian(rng) * grainSigma), 0f, 1f);
                    b = Math.Clamp(b + (float)(NextGaussian(rng) * grainSigma), 0f, 1f);
                }

                output.SetPixel(x, y, r, g, b, a);
            }
        }

        return output;
    }

    private static float EstimateGrainSigma(RasterImage image, DamageMask mask, float threshold)
    {
        double sumSquares = 0;
        int count = 0;

        for (int y = 1; y < image.Height - 1; y++)
        {
            for (int x = 1; x < image.Width - 1; x++)
            {
                if (mask.IsRepairable(x, y, threshold))
                {
                    continue; // Only measure grain where the photo's own detail is trustworthy.
                }

                float center = image.GetLuminance(x, y);
                float neighbourAverage = (
                    image.GetLuminance(x - 1, y) + image.GetLuminance(x + 1, y) +
                    image.GetLuminance(x, y - 1) + image.GetLuminance(x, y + 1)) / 4f;

                float residual = center - neighbourAverage; // A crude high-pass: the local "grain" signal.
                sumSquares += residual * residual;
                count++;
            }
        }

        return count == 0 ? 0f : (float)Math.Sqrt(sumSquares / count);
    }

    private static double NextGaussian(Random rng)
    {
        // Box-Muller transform.
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}
