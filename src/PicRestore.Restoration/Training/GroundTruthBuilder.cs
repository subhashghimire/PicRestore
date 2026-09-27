using PicRestore.Core.Imaging;
using PicRestore.Restoration.DamageDetection;
using PicRestore.Restoration.Imaging;

namespace PicRestore.Restoration.Training;

/// <summary>
/// Turns a (damaged scan, restored version) pair into a <see cref="TrainingPair"/>: aligns the restored
/// image into the scan's frame, removes the restorer's overall colour grading with a robust global
/// colour mapping, and labels as damage the pixels that still differ clearly. Mirrors
/// <c>tools/damage-model/make_pair.py</c>.
///
/// <para>
/// Restoration tools that re-synthesise texture (foliage, soil, fabric) differ from the scan there even
/// where nothing was damaged, so the damage threshold rises with the reference's own local texture:
/// a speck on a smooth wall counts, a re-drawn leaf pattern doesn't. Some reinterpretation (e.g. a skirt
/// recoloured from black to navy) can still slip through - which is why the app shows every pair's
/// damage map for review before it is used.
/// </para>
/// </summary>
public static class GroundTruthBuilder
{
    public const float ResidualThreshold = 0.10f;
    public const float TextureFactor = 1.5f;

    public static TrainingPair Build(string name, RasterImage damaged, RasterImage restored, PairAlignment alignment)
    {
        ArgumentNullException.ThrowIfNull(damaged);
        ArgumentNullException.ThrowIfNull(restored);

        int width = damaged.Width, height = damaged.Height;
        (int ww, int wh) = WorkingSize(width, height, LearnedDamageDetector.WorkingLongSide);
        (int fw, int fh) = WorkingSize(width, height, 2 * Math.Max(ww, wh));
        int nf = fw * fh;

        // Damaged scan at the fine analysis resolution and at working resolution.
        (float[] dr, float[] dg, float[] db) = Planar(damaged);
        float[] fr = Down(dr, width, height, fw, fh), fg = Down(dg, width, height, fw, fh), fb = Down(db, width, height, fw, fh);

        // Restored image resampled into the same frame.
        (float[] rr, float[] rg, float[] rb, bool[] valid) = PairAligner.Warp(restored, alignment, fw, fh);
        bool[] known = Erode(valid, fw, fh, 2);

        // Robust global colour mapping damaged -> restored (2nd-order polynomial per channel).
        double[][] coefficients = FitColourMapping(fr, fg, fb, rr, rg, rb, known);
        var mr = new float[nf];
        var mg = new float[nf];
        var mb = new float[nf];
        var terms = new double[10];
        for (int i = 0; i < nf; i++)
        {
            Terms(fr[i], fg[i], fb[i], terms);
            mr[i] = Math.Clamp((float)Dot(coefficients[0], terms), 0f, 1f);
            mg[i] = Math.Clamp((float)Dot(coefficients[1], terms), 0f, 1f);
            mb[i] = Math.Clamp((float)Dot(coefficients[2], terms), 0f, 1f);
        }

        // Residual after the colour match, against a texture-adaptive threshold.
        float[] residual = new float[nf];
        float[][] blurredMapped = { ImageOps.GaussianBlur(mr, fw, fh, 1.5), ImageOps.GaussianBlur(mg, fw, fh, 1.5), ImageOps.GaussianBlur(mb, fw, fh, 1.5) };
        float[][] blurredRef = { ImageOps.GaussianBlur(rr, fw, fh, 1.5), ImageOps.GaussianBlur(rg, fw, fh, 1.5), ImageOps.GaussianBlur(rb, fw, fh, 1.5) };
        for (int i = 0; i < nf; i++)
        {
            residual[i] = (MathF.Abs(blurredMapped[0][i] - blurredRef[0][i]) + MathF.Abs(blurredMapped[1][i] - blurredRef[1][i])
                + MathF.Abs(blurredMapped[2][i] - blurredRef[2][i])) / 3f;
        }

        var refL = new float[nf];
        var refL2 = new float[nf];
        for (int i = 0; i < nf; i++)
        {
            refL[i] = (0.2126f * rr[i]) + (0.7152f * rg[i]) + (0.0722f * rb[i]);
            refL2[i] = refL[i] * refL[i];
        }

        float[] mean = ImageOps.BoxMean(refL, fw, fh, 3);
        float[] mean2 = ImageOps.BoxMean(refL2, fw, fh, 3);
        var damage = new bool[nf];
        for (int i = 0; i < nf; i++)
        {
            float texture = MathF.Sqrt(MathF.Max(mean2[i] - (mean[i] * mean[i]), 0f));
            damage[i] = valid[i] && residual[i] > MathF.Max(ResidualThreshold, TextureFactor * texture);
        }

        damage = Dilate(Erode(damage, fw, fh, 1), fw, fh, 1);   // opening: drop isolated noise pixels
        damage = Erode(Dilate(damage, fw, fh, 2), fw, fh, 2);   // closing: fill pinholes inside blotches

        // Down to working resolution.
        float[] damageW = Down(ToFloat(damage), fw, fh, ww, wh);
        float[] knownW = Down(ToFloat(known), fw, fh, ww, wh);
        int nw = ww * wh;
        var damageMask = new bool[nw];
        var knownMask = new bool[nw];
        for (int i = 0; i < nw; i++)
        {
            damageMask[i] = damageW[i] > 0.5f;
            knownMask[i] = knownW[i] > 0.999f;
        }

        return new TrainingPair
        {
            Name = name,
            Width = ww,
            Height = wh,
            R = Down(dr, width, height, ww, wh),
            G = Down(dg, width, height, ww, wh),
            B = Down(db, width, height, ww, wh),
            Damage = damageMask,
            Known = knownMask,
        };
    }

    /// <summary>The pair's scan with its damage labels tinted red and unknown (uncovered) areas dimmed - for review.</summary>
    public static RasterImage Preview(TrainingPair pair)
    {
        var image = new RasterImage(pair.Width, pair.Height);
        for (int i = 0; i < pair.Width * pair.Height; i++)
        {
            float r = pair.R[i], g = pair.G[i], b = pair.B[i];
            if (!pair.Known[i])
            {
                r *= 0.35f; g *= 0.35f; b *= 0.35f;
            }
            else if (pair.Damage[i])
            {
                r = (0.4f * r) + 0.6f;
                g *= 0.4f;
                b *= 0.4f;
            }

            image.Pixels[i * 4] = r;
            image.Pixels[(i * 4) + 1] = g;
            image.Pixels[(i * 4) + 2] = b;
            image.Pixels[(i * 4) + 3] = 1f;
        }

        return image;
    }

    public static (int Width, int Height) WorkingSize(int width, int height, int longSide)
    {
        double scale = Math.Min(1.0, (double)longSide / Math.Max(width, height));
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static double[][] FitColourMapping(float[] fr, float[] fg, float[] fb, float[] rr, float[] rg, float[] rb, bool[] known)
    {
        var candidates = new List<int>();
        for (int i = 0; i < known.Length; i++)
        {
            if (known[i])
            {
                candidates.Add(i);
            }
        }

        if (candidates.Count < 1000)
        {
            throw new InvalidOperationException("The restored image overlaps too little of the scan to learn from.");
        }

        var rng = new Random(0);
        int count = Math.Min(300_000, candidates.Count);
        var sample = new int[count];
        for (int k = 0; k < count; k++)
        {
            sample[k] = candidates[rng.Next(candidates.Count)];
        }

        var use = Enumerable.Repeat(true, count).ToArray();
        double[][] coefficients = new double[3][];
        var terms = new double[10];
        for (int pass = 0; pass < 3; pass++)
        {
            for (int c = 0; c < 3; c++)
            {
                float[] target = c == 0 ? rr : c == 1 ? rg : rb;
                var ata = new double[10, 10];
                var atb = new double[10];
                for (int k = 0; k < count; k++)
                {
                    if (!use[k])
                    {
                        continue;
                    }

                    int i = sample[k];
                    Terms(fr[i], fg[i], fb[i], terms);
                    for (int p = 0; p < 10; p++)
                    {
                        atb[p] += terms[p] * target[i];
                        for (int q = p; q < 10; q++)
                        {
                            ata[p, q] += terms[p] * terms[q];
                        }
                    }
                }

                coefficients[c] = Solve(ata, atb);
            }

            // Keep the 60% best-fitting samples for the next pass (damage and reinterpretation are outliers).
            var errors = new double[count];
            for (int k = 0; k < count; k++)
            {
                int i = sample[k];
                Terms(fr[i], fg[i], fb[i], terms);
                errors[k] = (Math.Abs(Dot(coefficients[0], terms) - rr[i]) + Math.Abs(Dot(coefficients[1], terms) - rg[i])
                    + Math.Abs(Dot(coefficients[2], terms) - rb[i])) / 3;
            }

            double cut = errors.OrderBy(e => e).ElementAt((int)(0.6 * (count - 1)));
            for (int k = 0; k < count; k++)
            {
                use[k] = errors[k] < cut;
            }
        }

        return coefficients;
    }

    private static void Terms(float r, float g, float b, double[] t)
    {
        t[0] = 1; t[1] = r; t[2] = g; t[3] = b;
        t[4] = r * r; t[5] = g * g; t[6] = b * b;
        t[7] = r * g; t[8] = r * b; t[9] = g * b;
    }

    private static double Dot(double[] a, double[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++)
        {
            s += a[i] * b[i];
        }

        return s;
    }

    /// <summary>Solves the (upper-triangle-filled) symmetric normal equations with a tiny ridge, by Gaussian elimination.</summary>
    private static double[] Solve(double[,] ataUpper, double[] atb)
    {
        int n = atb.Length;
        var a = new double[n, n + 1];
        for (int p = 0; p < n; p++)
        {
            for (int q = 0; q < n; q++)
            {
                a[p, q] = q >= p ? ataUpper[p, q] : ataUpper[q, p];
            }

            a[p, p] += 1e-6;
            a[p, n] = atb[p];
        }

        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            for (int row = col + 1; row < n; row++)
            {
                if (Math.Abs(a[row, col]) > Math.Abs(a[pivot, col]))
                {
                    pivot = row;
                }
            }

            for (int k = 0; k <= n; k++)
            {
                (a[col, k], a[pivot, k]) = (a[pivot, k], a[col, k]);
            }

            for (int row = 0; row < n; row++)
            {
                if (row == col || a[col, col] == 0)
                {
                    continue;
                }

                double f = a[row, col] / a[col, col];
                for (int k = col; k <= n; k++)
                {
                    a[row, k] -= f * a[col, k];
                }
            }
        }

        var x = new double[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = a[i, i] == 0 ? 0 : a[i, n] / a[i, i];
        }

        return x;
    }

    private static (float[] R, float[] G, float[] B) Planar(RasterImage image)
    {
        int n = image.Width * image.Height;
        var r = new float[n];
        var g = new float[n];
        var b = new float[n];
        for (int i = 0; i < n; i++)
        {
            r[i] = image.Pixels[i * 4];
            g[i] = image.Pixels[(i * 4) + 1];
            b[i] = image.Pixels[(i * 4) + 2];
        }

        return (r, g, b);
    }

    private static float[] Down(float[] plane, int w, int h, int ow, int oh) =>
        ow == w && oh == h ? (float[])plane.Clone() : ImageOps.ResizeArea(plane, w, h, ow, oh);

    private static float[] ToFloat(bool[] mask) => mask.Select(v => v ? 1f : 0f).ToArray();

    /// <summary>Binary erosion with a 4-connected cross, repeated; outside the image counts as "off".</summary>
    private static bool[] Erode(bool[] src, int w, int h, int iterations)
    {
        bool[] current = src;
        for (int it = 0; it < iterations; it++)
        {
            var next = new bool[current.Length];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w) + x;
                    next[i] = current[i] && x > 0 && current[i - 1] && x < w - 1 && current[i + 1]
                        && y > 0 && current[i - w] && y < h - 1 && current[i + w];
                }
            }

            current = next;
        }

        return current;
    }

    /// <summary>Binary dilation with a 4-connected cross, repeated.</summary>
    private static bool[] Dilate(bool[] src, int w, int h, int iterations)
    {
        bool[] current = src;
        for (int it = 0; it < iterations; it++)
        {
            var next = new bool[current.Length];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w) + x;
                    next[i] = current[i] || (x > 0 && current[i - 1]) || (x < w - 1 && current[i + 1])
                        || (y > 0 && current[i - w]) || (y < h - 1 && current[i + w]);
                }
            }

            current = next;
        }

        return current;
    }
}
