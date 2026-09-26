using PicRestore.Core.Imaging;

namespace PicRestore.Tests;

/// <summary>Small, deterministic synthetic images so the pipeline's unit tests don't depend on real photo files.</summary>
internal static class TestImages
{
    /// <summary>
    /// A mid-grey image with optional deterministic pseudo-grain noise. Real photos always carry some
    /// grain, so tests that assert "this isn't flagged as damage" use a small noiseSigma rather than a
    /// perfectly flat field - a perfectly flat region is a synthetic corner case the detector
    /// intentionally treats as suspiciously smooth (see CompositeDamageDetector's FadedLowContrast rule).
    /// </summary>
    public static RasterImage CreateFlat(int width, int height, float gray = 0.5f, float noiseSigma = 0f, int seed = 1)
    {
        var image = new RasterImage(width, height);
        var rng = new Random(seed);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float noise = noiseSigma > 0f ? (float)((rng.NextDouble() - 0.5) * 2 * noiseSigma) : 0f;
                float value = Math.Clamp(gray + noise, 0f, 1f);
                image.SetPixel(x, y, value, value, value, 1f);
            }
        }

        return image;
    }

    /// <summary>A flat mid-grey image with a solid, near-white, flat square stamped on it - a synthetic "bleached patch".</summary>
    public static RasterImage WithWhitePatch(int width, int height, int patchX, int patchY, int patchSize)
    {
        RasterImage image = CreateFlat(width, height);

        for (int y = patchY; y < patchY + patchSize && y < height; y++)
        {
            for (int x = patchX; x < patchX + patchSize && x < width; x++)
            {
                image.SetPixel(x, y, 0.98f, 0.98f, 0.98f, 1f);
            }
        }

        return image;
    }
}
