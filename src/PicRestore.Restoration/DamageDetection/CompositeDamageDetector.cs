using PicRestore.Core.Abstractions;
using PicRestore.Core.Imaging;
using PicRestore.Core.Models;

namespace PicRestore.Restoration.DamageDetection;

/// <summary>
/// Classical, dependency-free damage detector for the Phase-1 MVP pipeline (see the design plan's
/// roadmap). It combines a few signal-processing cues - luminance outliers, local contrast, and a
/// simple ridge/line response - into a single per-pixel damage probability and a best-guess damage
/// type. It is deliberately tuned to favour precision over recall: better to miss a faint scratch
/// (the user can paint it in) than flag an intact area (which risks that area being repainted).
///
/// Global colour-cast/yellowing is intentionally NOT flagged here as a per-pixel "damage" region: per
/// the design plan, that is a whole-photo tone correction handled by <see cref="Color.HistogramColorRestorer"/>
/// regardless of the mask, not something to reconstruct. This detector only marks pixels that need a
/// repair (missing content or collapsed contrast).
///
/// Phase 2 (per the roadmap) replaces this with a purpose-trained segmentation model exported to
/// ONNX; this detector's job in the meantime is to make the workflow usable and testable end to end.
/// </summary>
public sealed class CompositeDamageDetector : IDamageDetector
{
    // Half-width of the local statistics window (a (2r+1) x (2r+1) box).
    private const int WindowRadius = 3;

    public DamageMask Detect(RasterImage image, ProcessingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(settings);

        int width = image.Width;
        int height = image.Height;

        var luminance = new float[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                luminance[(y * width) + x] = image.GetLuminance(x, y);
            }
        }

        var stats = new LocalStatistics(luminance, width, height, WindowRadius);
        var mask = new DamageMask(width, height);

        // Sensitivity in [0,1] shifts every threshold. Higher sensitivity flags more.
        float sensitivity = Math.Clamp(settings.DetectionSensitivity, 0f, 1f);
        float whiteLuminanceFloor = 0.90f - (0.15f * sensitivity);     // 0.90 (low sens.) .. 0.75 (high sens.)
        float lowVarianceCeiling = 0.0015f + (0.0035f * sensitivity);  // 0.0015 .. 0.0050
        float fadedVarianceCeiling = lowVarianceCeiling * 0.6f;
        float ridgeThreshold = 0.35f - (0.20f * sensitivity);          // 0.35 .. 0.15

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float l = luminance[(y * width) + x];
                float localVar = stats.LocalVariance(x, y);
                float ridge = RidgeResponse(luminance, width, height, x, y);

                // 1) White / bleached patch: bright and locally flat - the photo's texture has been wiped out.
                if (l >= whiteLuminanceFloor && localVar <= lowVarianceCeiling)
                {
                    float probability = Blend(
                        Normalize(l, whiteLuminanceFloor, 1f),
                        Normalize(lowVarianceCeiling - localVar, 0f, lowVarianceCeiling));
                    mask.SetDamage(x, y, probability, DamageType.WhiteOrBleachedPatch);
                    continue;
                }

                // 2) Crease / scratch / tear: a thin, strong discontinuity against a locally smoother field.
                if (ridge >= ridgeThreshold)
                {
                    mask.SetDamage(x, y, Normalize(ridge, ridgeThreshold, 1f), DamageType.CreaseScratchOrTear);
                    continue;
                }

                // 3) Faded / low-contrast: unusually flat locally but not bright enough to be a bleached
                //    patch - dynamic range has collapsed without content actually being destroyed.
                if (localVar <= fadedVarianceCeiling && l is > 0.15f and < 0.85f)
                {
                    float probability = Normalize(fadedVarianceCeiling - localVar, 0f, fadedVarianceCeiling) * 0.6f;
                    if (probability > 0f)
                    {
                        mask.SetDamage(x, y, probability, DamageType.FadedLowContrast);
                    }
                }
            }
        }

        return mask;
    }

    private static float RidgeResponse(float[] luminance, int width, int height, int x, int y)
    {
        // A simple discrete Laplacian magnitude, clamped to the image bounds. Acts as a proxy for thin
        // bright/dark line defects (scratches, hairline cracks) without needing multi-orientation filters.
        float center = Sample(luminance, width, height, x, y);
        float neighbours = Sample(luminance, width, height, x - 1, y)
            + Sample(luminance, width, height, x + 1, y)
            + Sample(luminance, width, height, x, y - 1)
            + Sample(luminance, width, height, x, y + 1);

        float laplacian = MathF.Abs((4f * center) - neighbours);
        return Math.Clamp(laplacian, 0f, 1f);
    }

    private static float Sample(float[] luminance, int width, int height, int x, int y)
    {
        x = Math.Clamp(x, 0, width - 1);
        y = Math.Clamp(y, 0, height - 1);
        return luminance[(y * width) + x];
    }

    private static float Normalize(float value, float lo, float hi)
    {
        if (hi <= lo)
        {
            return value >= hi ? 1f : 0f;
        }

        return Math.Clamp((value - lo) / (hi - lo), 0f, 1f);
    }

    private static float Blend(float a, float b) => (a + b) / 2f;
}
