using PicRestore.Core.Imaging;
using PicRestore.Restoration.Imaging;

namespace PicRestore.Restoration.Training;

/// <summary>
/// Where a restored image sits inside the damaged scan's frame: restored normalised coordinates (0..1)
/// map to damaged normalised coordinates as x_d = ScaleX * x_r + OffsetX (same for y). Restoration tools
/// commonly rescale by a few percent and crop to a different aspect ratio; this captures both.
/// </summary>
public readonly record struct PairAlignment(double ScaleX, double ScaleY, double OffsetX, double OffsetY, double Score);

/// <summary>
/// Finds <see cref="PairAlignment"/> without feature matching: gradient-magnitude images are compared
/// with normalised cross-correlation, first by a coarse translation search at the scale implied by the
/// two aspect ratios, then by coordinate-descent refinement of all four parameters at 192, 384 and 768 px.
/// Measured against a SIFT + RANSAC homography on five real pairs: within 0.1-2.6 px at 1280 px width.
/// </summary>
public static class PairAligner
{
    /// <summary>Below this correlation the images probably aren't the same photo.</summary>
    public const double MinimumScore = 0.3;

    public static PairAlignment Align(RasterImage damaged, RasterImage restored, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(damaged);
        ArgumentNullException.ThrowIfNull(restored);

        double aspectD = (double)damaged.Width / damaged.Height;
        double aspectR = (double)restored.Width / restored.Height;
        double sx = 1, sy = 1;
        if (aspectR > aspectD)
        {
            sy = aspectD / aspectR; // restored is a horizontal strip of the damaged frame
        }
        else
        {
            sx = aspectR / aspectD;
        }

        // Coarse translation search.
        var coarseD = GradientImage.Create(damaged, 96);
        var coarseR = GradientImage.Create(restored, 96);
        double[] best = { sx, sy, 0, 0 };
        double bestScore = double.NegativeInfinity;
        foreach (double s in new[] { 0.85, 0.88, 0.91, 0.94, 0.97, 1.0, 1.03 })
        {
            double csx = sx * s, csy = sy * s;
            for (double tx = -0.1; tx <= 1 - csx + 0.1 + 1e-9; tx += 1.0 / coarseD.Width)
            {
                for (double ty = -0.1; ty <= 1 - csy + 0.1 + 1e-9; ty += 1.0 / coarseD.Height)
                {
                    double score = Score(coarseD, coarseR, csx, csy, tx, ty);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = new[] { csx, csy, tx, ty };
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        // Refinement.
        foreach ((int size, double step0) in new[] { (192, 0.01), (384, 0.004), (768, 0.0015) })
        {
            var d = GradientImage.Create(damaged, size);
            var r = GradientImage.Create(restored, size);
            double current = Score(d, r, best[0], best[1], best[2], best[3]);
            double step = step0;
            for (int iteration = 0; iteration < 40; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool improved = false;
                for (int k = 0; k < 4; k++)
                {
                    foreach (int direction in new[] { 1, -1 })
                    {
                        var candidate = (double[])best.Clone();
                        candidate[k] += direction * step;
                        double score = Score(d, r, candidate[0], candidate[1], candidate[2], candidate[3]);
                        if (score > current)
                        {
                            current = score;
                            best = candidate;
                            improved = true;
                        }
                    }
                }

                if (!improved)
                {
                    step /= 2;
                    if (step < step0 / 8)
                    {
                        break;
                    }
                }
            }

            bestScore = current;
        }

        return new PairAlignment(best[0], best[1], best[2], best[3], bestScore);
    }

    /// <summary>
    /// Resamples <paramref name="restored"/> into the damaged frame at <paramref name="width"/> x
    /// <paramref name="height"/> (planar RGB) and reports which output pixels it actually covers.
    /// </summary>
    public static (float[] R, float[] G, float[] B, bool[] Valid) Warp(RasterImage restored, PairAlignment a, int width, int height)
    {
        int n = width * height;
        var r = new float[n];
        var g = new float[n];
        var b = new float[n];
        var valid = new bool[n];
        int rw = restored.Width, rh = restored.Height;
        float[] px = restored.Pixels;
        Parallel.For(0, height, y =>
        {
            double v = (((y + 0.5) / height) - a.OffsetY) / a.ScaleY;
            double fy = (v * rh) - 0.5;
            for (int x = 0; x < width; x++)
            {
                double u = (((x + 0.5) / width) - a.OffsetX) / a.ScaleX;
                double fx = (u * rw) - 0.5;
                if (fx < 0 || fy < 0 || fx > rw - 1 || fy > rh - 1)
                {
                    continue;
                }

                int x0 = Math.Min((int)fx, rw - 2 < 0 ? 0 : rw - 2);
                int y0 = Math.Min((int)fy, rh - 2 < 0 ? 0 : rh - 2);
                int x1 = Math.Min(x0 + 1, rw - 1), y1 = Math.Min(y0 + 1, rh - 1);
                float tx = (float)(fx - x0), ty = (float)(fy - y0);
                int i = (y * width) + x;
                for (int c = 0; c < 3; c++)
                {
                    float p00 = px[(((y0 * rw) + x0) * 4) + c], p01 = px[(((y0 * rw) + x1) * 4) + c];
                    float p10 = px[(((y1 * rw) + x0) * 4) + c], p11 = px[(((y1 * rw) + x1) * 4) + c];
                    float value = ((p00 * (1 - tx)) + (p01 * tx)) * (1 - ty) + (((p10 * (1 - tx)) + (p11 * tx)) * ty);
                    if (c == 0) r[i] = value; else if (c == 1) g[i] = value; else b[i] = value;
                }

                valid[i] = true;
            }
        });

        return (r, g, b, valid);
    }

    private static double Score(GradientImage d, GradientImage r, double sx, double sy, double tx, double ty)
    {
        double sumA = 0, sumB = 0, sumAA = 0, sumBB = 0, sumAB = 0;
        long count = 0;
        for (int y = 0; y < d.Height; y++)
        {
            double v = (((y + 0.5) / d.Height) - ty) / sy;
            double fy = (v * r.Height) - 0.5;
            if (fy < 0 || fy > r.Height - 1)
            {
                continue;
            }

            int y0 = Math.Min((int)fy, r.Height - 2);
            double wy = fy - y0;
            for (int x = 0; x < d.Width; x++)
            {
                double u = (((x + 0.5) / d.Width) - tx) / sx;
                double fx = (u * r.Width) - 0.5;
                if (fx < 0 || fx > r.Width - 1)
                {
                    continue;
                }

                int x0 = Math.Min((int)fx, r.Width - 2);
                double wx = fx - x0;
                int i0 = (y0 * r.Width) + x0;
                double top = r.Values[i0] + ((r.Values[i0 + 1] - r.Values[i0]) * wx);
                double bottom = r.Values[i0 + r.Width] + ((r.Values[i0 + r.Width + 1] - r.Values[i0 + r.Width]) * wx);
                double bv = top + ((bottom - top) * wy);
                double av = d.Values[(y * d.Width) + x];
                sumA += av;
                sumB += bv;
                sumAA += av * av;
                sumBB += bv * bv;
                sumAB += av * bv;
                count++;
            }
        }

        if (count < 0.4 * d.Width * d.Height)
        {
            return -1;
        }

        double covariance = sumAB - (sumA * sumB / count);
        double varianceA = sumAA - (sumA * sumA / count);
        double varianceB = sumBB - (sumB * sumB / count);
        return covariance / Math.Sqrt((varianceA * varianceB) + 1e-12);
    }

    /// <summary>Gradient magnitude of the luminance, area-downscaled so the long side is a given size.</summary>
    private sealed class GradientImage
    {
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required float[] Values { get; init; }

        public static GradientImage Create(RasterImage image, int longSide)
        {
            double scale = Math.Min(1.0, (double)longSide / Math.Max(image.Width, image.Height));
            int w = Math.Max(8, (int)Math.Round(image.Width * scale));
            int h = Math.Max(8, (int)Math.Round(image.Height * scale));
            int n = image.Width * image.Height;
            var l = new float[n];
            for (int i = 0; i < n; i++)
            {
                l[i] = (0.2126f * image.Pixels[i * 4]) + (0.7152f * image.Pixels[(i * 4) + 1]) + (0.0722f * image.Pixels[(i * 4) + 2]);
            }

            float[] small = w == image.Width && h == image.Height ? l : ImageOps.ResizeArea(l, image.Width, image.Height, w, h);
            var grad = new float[w * h];
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    int i = (y * w) + x;
                    float gx = small[i + 1] - small[i - 1];
                    float gy = small[i + w] - small[i - w];
                    grad[i] = MathF.Sqrt((gx * gx) + (gy * gy));
                }
            }

            return new GradientImage { Width = w, Height = h, Values = grad };
        }
    }
}
