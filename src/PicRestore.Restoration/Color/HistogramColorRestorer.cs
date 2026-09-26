using PicRestore.Core.Abstractions;
using PicRestore.Core.Imaging;
using PicRestore.Core.Models;

namespace PicRestore.Restoration.Color;

/// <summary>
/// Classical colour and tone restoration: a grey-world white-balance re-estimate plus a bounded
/// contrast stretch, both computed from the photo's own undamaged pixels and blended in by
/// <see cref="ProcessingSettings.ColorRestorationStrength"/>. Deliberately global (not confined to the
/// damage mask) since ageing colour shifts are usually whole-photo, not a discrete region to repair.
///
/// Gain and contrast changes are clamped to a conservative range so the result stays inside the
/// era-appropriate palette rather than snapping to a clinical, modern white balance - see the design
/// plan's "Colour & Tonal Restoration" section for the reasoning.
/// </summary>
public sealed class HistogramColorRestorer : IColorRestorer
{
    private const float MinGain = 0.7f;
    private const float MaxGain = 1.4f;
    private const float DamageIgnoreThreshold = 0.5f;

    public RasterImage Restore(RasterImage image, DamageMask mask, ProcessingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(settings);

        float strength = Math.Clamp(settings.ColorRestorationStrength, 0f, 1f);
        if (strength <= 0f)
        {
            return image.Clone();
        }

        (float avgR, float avgG, float avgB, float avgL, float loL, float hiL) = SampleUndamagedStatistics(image, mask);

        // Grey-world gain per channel, softened toward 1.0 (no change) by strength and clamped so the
        // correction can never fully neutralise the era-appropriate cast.
        float gainR = ClampGain(Lerp(1f, SafeDivide(avgL, avgR), strength));
        float gainG = ClampGain(Lerp(1f, SafeDivide(avgL, avgG), strength));
        float gainB = ClampGain(Lerp(1f, SafeDivide(avgL, avgB), strength));

        // Contrast stretch toward the surviving highlight/shadow range, also softened by strength.
        float stretchLo = Lerp(0f, loL, strength);
        float stretchHi = Lerp(1f, hiL, strength);
        float range = Math.Max(stretchHi - stretchLo, 0.05f);

        var output = new RasterImage(image.Width, image.Height);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                (float r, float g, float b, float a) = image.GetPixel(x, y);

                float correctedR = Stretch(Math.Clamp(r * gainR, 0f, 1f), stretchLo, range);
                float correctedG = Stretch(Math.Clamp(g * gainG, 0f, 1f), stretchLo, range);
                float correctedB = Stretch(Math.Clamp(b * gainB, 0f, 1f), stretchLo, range);

                output.SetPixel(x, y, correctedR, correctedG, correctedB, a);
            }
        }

        return output;
    }

    private static (float AvgR, float AvgG, float AvgB, float AvgL, float LoL, float HiL) SampleUndamagedStatistics(
        RasterImage image, DamageMask mask)
    {
        double sumR = 0, sumG = 0, sumB = 0;
        int count = 0;
        var luminances = new List<float>((image.Width * image.Height / 4) + 1);

        void Accumulate(int x, int y)
        {
            (float r, float g, float b, _) = image.GetPixel(x, y);
            sumR += r;
            sumG += g;
            sumB += b;
            luminances.Add(image.GetLuminance(x, y));
            count++;
        }

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                int i = mask.IndexOf(x, y);
                if (mask.Probability[i] < DamageIgnoreThreshold)
                {
                    Accumulate(x, y); // Only undamaged pixels get to bias the colour reference.
                }
            }
        }

        if (count == 0)
        {
            // The whole photo was flagged as damaged - fall back to global statistics rather than
            // divide by zero; a strength slider at 0 remains the user's escape hatch in that case.
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    Accumulate(x, y);
                }
            }
        }

        luminances.Sort();

        float Percentile(float p)
        {
            int idx = Math.Clamp((int)(p * (luminances.Count - 1)), 0, luminances.Count - 1);
            return luminances[idx];
        }

        float avgR = (float)(sumR / count);
        float avgG = (float)(sumG / count);
        float avgB = (float)(sumB / count);
        float avgL = (0.2126f * avgR) + (0.7152f * avgG) + (0.0722f * avgB);

        return (avgR, avgG, avgB, avgL, Percentile(0.01f), Percentile(0.99f));
    }

    private static float Stretch(float value, float lo, float range) =>
        Math.Clamp((value - lo) / range, 0f, 1f);

    private static float SafeDivide(float numerator, float denominator) =>
        denominator <= 1e-4f ? 1f : numerator / denominator;

    private static float ClampGain(float gain) => Math.Clamp(gain, MinGain, MaxGain);

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);
}
