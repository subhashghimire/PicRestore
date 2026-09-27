using PicRestore.Core.Abstractions;
using PicRestore.Core.Imaging;
using PicRestore.Core.Models;

namespace PicRestore.Restoration.DamageDetection;

/// <summary>
/// Classical, dependency-free damage detector for the Phase-1 MVP pipeline (see the design plan's
/// roadmap). It combines a few signal-processing cues - luminance outliers, local contrast, a simple
/// ridge/line response, and a regional tonal-anomaly score - into a single per-pixel damage
/// probability and a best-guess damage type. It is deliberately tuned to favour precision over recall:
/// better to miss a faint scratch (the user can paint it in) than flag an intact area (which risks
/// that area being repainted).
///
/// Global colour-cast/yellowing is intentionally NOT flagged here as a per-pixel "damage" region: per
/// the design plan, that is a whole-photo tone correction handled by <see cref="Color.HistogramColorRestorer"/>
/// regardless of the mask, not something to reconstruct. This detector only marks pixels that need a
/// repair (missing content or collapsed contrast).
///
/// <para>
/// A known, deliberate limitation (see the design plan's Phase 2 roadmap item on a trained
/// segmentation model): metallic/foxing speckle damage - and especially a LARGE, solidly flat patch of
/// it sitting on an equally flat, equally pale background (e.g. a bleached blotch on plain pavement) -
/// is not reliably separable from genuine photo content using local pixel statistics alone. Its
/// interior is, by every local intensity/texture/colour measure, indistinguishable from a plausible
/// bright, low-contrast, desaturated surface; there is no per-pixel or small-window signal that tells
/// them apart without shape-level or learned-model reasoning. The <see cref="DamageType.MetallicOrFoxingSpeckle"/>
/// rule below catches this damage's textured EDGES and small-to-medium isolated speckles (both have a
/// measurable local signature - see remarks on that rule) but will generally miss the deep interior of
/// a large, smoothly-flat damaged region. For those, the practical workaround today is the Mask
/// Editor's manual damage brush.
/// </para>
///
/// Phase 2 (per the roadmap) replaces this with a purpose-trained segmentation model exported to
/// ONNX; this detector's job in the meantime is to make the workflow usable and testable end to end.
/// </summary>
public sealed class CompositeDamageDetector : IDamageDetector
{
    // Half-width of the local (fine) statistics window (a (2r+1) x (2r+1) box) - the same scale the
    // original detector used for its white-patch and ridge rules.
    private const int WindowRadius = 3;

    // Half-width of the REGIONAL context window used by the tonal-anomaly rule below. Deliberately much
    // larger than WindowRadius: the question that window answers is "does this small area look like an
    // outlier compared to its own neighbourhood", which only works if the neighbourhood is big enough to
    // mostly contain undamaged surroundings rather than more of the same damage.
    private const int RegionalRadius = 20;

    public DamageMask Detect(RasterImage image, ProcessingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(settings);

        int width = image.Width;
        int height = image.Height;
        int count = width * height;

        var luminance = new float[count];
        var saturation = new float[count];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width) + x;
                luminance[i] = image.GetLuminance(x, y);

                (float r, float g, float b, _) = image.GetPixel(x, y);
                float maxc = MathF.Max(r, MathF.Max(g, b));
                float minc = MathF.Min(r, MathF.Min(g, b));
                saturation[i] = maxc > 0f ? (maxc - minc) / maxc : 0f;
            }
        }

        var stats = new LocalStatistics(luminance, width, height, WindowRadius);
        var regionalLuminance = new LocalStatistics(luminance, width, height, RegionalRadius);
        var regionalSaturation = new LocalStatistics(saturation, width, height, RegionalRadius);
        var mask = new DamageMask(width, height);

        // Sensitivity in [0,1] shifts every threshold. Higher sensitivity flags more.
        float sensitivity = Math.Clamp(settings.DetectionSensitivity, 0f, 1f);
        float whiteLuminanceFloor = 0.90f - (0.15f * sensitivity);     // 0.90 (low sens.) .. 0.75 (high sens.)
        float lowVarianceCeiling = 0.0015f + (0.0035f * sensitivity);  // 0.0015 .. 0.0050
        float fadedVarianceCeiling = lowVarianceCeiling * 0.6f;
        float ridgeThreshold = 0.35f - (0.20f * sensitivity);          // 0.35 .. 0.15

        // Pass 1: precompute the tonal-anomaly score everywhere, so its threshold can be chosen
        // adaptively (as a percentile of THIS photo's own scores) rather than as a fixed constant that
        // would only suit one particular exposure/scan. See the class remarks for what this rule is for
        // and what it deliberately does not catch.
        var localVariance = new float[count];
        var anomalyScore = new float[count];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width) + x;
                localVariance[i] = stats.LocalVariance(x, y);

                float brightSpike = MathF.Max(0f, luminance[i] - regionalLuminance.LocalMean(x, y));
                float desatSpike = MathF.Max(0f, regionalSaturation.LocalMean(x, y) - saturation[i]);
                anomalyScore[i] = brightSpike * desatSpike * MathF.Sqrt(localVariance[i]);
            }
        }

        // Higher sensitivity widens the flagged share from ~3% to ~10% of the photo's pixels.
        float anomalyTopFraction = 0.03f + (0.07f * sensitivity);
        float anomalyThreshold = Percentile(anomalyScore, 1f - anomalyTopFraction);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width) + x;
                float l = luminance[i];
                float localVar = localVariance[i];
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

                // 3) Metallic / foxing speckle: this pixel is both brighter AND less saturated than its
                //    own wider surroundings, in a locally textured (not flat) way - the jagged, silvery
                //    "salt and pepper" or ring-edged pattern typical of foxing and mirroring damage on
                //    old prints. Unlike rule 1, this does not require an absolute brightness floor - it
                //    only requires standing out from ITS OWN neighbourhood, which is what makes it catch
                //    mid-tone speckle that rule 1 misses and rule 4 would otherwise wrongly wave through
                //    as merely "faded" (see the class remarks for why very large, uniformly flat damaged
                //    regions are still out of reach here).
                if (anomalyThreshold > 0f && anomalyScore[i] >= anomalyThreshold)
                {
                    float probability = Normalize(anomalyScore[i], anomalyThreshold, anomalyThreshold * 5f);
                    mask.SetDamage(x, y, probability, DamageType.MetallicOrFoxingSpeckle);
                    continue;
                }

                // 4) Faded / low-contrast: unusually flat locally but not bright enough to be a bleached
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

    /// <summary>
    /// The value below which the given fraction of <paramref name="values"/> falls (a simple sorted-copy
    /// percentile). Used to turn the tonal-anomaly score - whose raw magnitude depends heavily on this
    /// particular photo's exposure and scan quality - into an adaptive, photo-relative threshold instead
    /// of a fixed constant that would only suit one kind of scan.
    /// </summary>
    private static float Percentile(float[] values, float fraction)
    {
        var sorted = (float[])values.Clone();
        Array.Sort(sorted);
        int index = (int)Math.Clamp(fraction * (sorted.Length - 1), 0, sorted.Length - 1);
        return sorted[index];
    }
}
