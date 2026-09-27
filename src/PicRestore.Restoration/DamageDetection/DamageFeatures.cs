using PicRestore.Restoration.Imaging;

namespace PicRestore.Restoration.DamageDetection;

/// <summary>
/// The 34 per-pixel features the learned damage detector was trained on - colour, saturation,
/// opponent-colour channels, and centre-surround / local-contrast / gradient statistics at four scales.
/// All generic image statistics (nothing positional, nothing photo-specific). The order and maths here
/// must stay exactly in step with the training script's <c>features.py</c>; if either changes, the
/// model has to be retrained and <see cref="LearnedDamageModel"/> regenerated.
/// </summary>
internal static class DamageFeatures
{
    public static readonly int[] Radii = { 2, 6, 16, 40 };

    public const int Count = 6 + (6 * 4) + 4; // 34

    /// <summary>
    /// Computes the feature matrix for a planar RGB image (values 0..1) at working resolution.
    /// Returns a row-major (pixels x <see cref="Count"/>) array.
    /// </summary>
    public static float[] Compute(float[] r, float[] g, float[] b, int width, int height)
    {
        int n = width * height;
        var l = new float[n];
        var s = new float[n];
        var o1 = new float[n];
        var o2 = new float[n];
        var max = new float[n];
        var min = new float[n];
        var l2 = new float[n];
        for (int i = 0; i < n; i++)
        {
            float rr = r[i], gg = g[i], bb = b[i];
            l[i] = (0.2126f * rr) + (0.7152f * gg) + (0.0722f * bb);
            float mx = MathF.Max(rr, MathF.Max(gg, bb));
            float mn = MathF.Min(rr, MathF.Min(gg, bb));
            max[i] = mx;
            min[i] = mn;
            s[i] = mx > 1e-6f ? (mx - mn) / MathF.Max(mx, 1e-6f) : 0f;
            o1[i] = rr - gg;
            o2[i] = ((rr + gg) / 2f) - bb;
            l2[i] = l[i] * l[i];
        }

        // Central-difference gradient magnitude, zero on the border (matches the prototype).
        var grad = new float[n];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width) + x;
                float gx = x > 0 && x < width - 1 ? (l[i + 1] - l[i - 1]) / 2f : 0f;
                float gy = y > 0 && y < height - 1 ? (l[i + width] - l[i - width]) / 2f : 0f;
                grad[i] = MathF.Sqrt((gx * gx) + (gy * gy));
            }
        }

        var features = new float[n * Count];
        int column = 0;

        void Put(float[] values)
        {
            for (int i = 0; i < n; i++)
            {
                features[(i * Count) + column] = values[i];
            }

            column++;
        }

        void PutDifference(float[] values, float[] mean)
        {
            for (int i = 0; i < n; i++)
            {
                features[(i * Count) + column] = values[i] - mean[i];
            }

            column++;
        }

        Put(l);
        Put(s);
        Put(o1);
        Put(o2);
        Put(max);
        Put(min);

        foreach (int radius in Radii)
        {
            float[] meanL = ImageOps.BoxMean(l, width, height, radius);
            float[] meanL2 = ImageOps.BoxMean(l2, width, height, radius);
            PutDifference(l, meanL);

            var std = new float[n];
            for (int i = 0; i < n; i++)
            {
                std[i] = MathF.Sqrt(MathF.Max(meanL2[i] - (meanL[i] * meanL[i]), 0f));
            }

            Put(std);
            PutDifference(s, ImageOps.BoxMean(s, width, height, radius));
            PutDifference(o1, ImageOps.BoxMean(o1, width, height, radius));
            PutDifference(o2, ImageOps.BoxMean(o2, width, height, radius));
            Put(ImageOps.BoxMean(grad, width, height, radius));
        }

        foreach (int radius in new[] { 6, 40 })
        {
            Put(ImageOps.BoxMean(s, width, height, radius));
            Put(ImageOps.BoxMean(l, width, height, radius));
        }

        if (column != Count)
        {
            throw new InvalidOperationException($"Feature count mismatch: produced {column}, model expects {Count}.");
        }

        return features;
    }
}
