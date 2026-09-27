namespace PicRestore.Restoration.Imaging;

/// <summary>
/// Small, dependency-free raster helpers shared by the learned detector and the LaMa inpainter. All
/// operate on single-channel planar float buffers (row-major, length = width * height). Their exact
/// semantics (edge handling, pixel-centre alignment) deliberately match what the detector was trained
/// with - numpy/scipy "nearest" box filters, OpenCV-style bilinear/bicubic resizing - so the C#
/// pipeline reproduces the validated prototype rather than an approximation of it.
/// </summary>
public static class ImageOps
{
    /// <summary>
    /// Exact area-average downscale (each output pixel is the overlap-weighted mean of the input pixels
    /// it covers). Only meaningful for downscaling; callers upscale with <see cref="ResizeBilinear"/> or
    /// <see cref="ResizeBicubic"/>.
    /// </summary>
    public static float[] ResizeArea(float[] src, int width, int height, int outWidth, int outHeight)
    {
        (int[] xStart, float[][] xWeights) = AreaWeights(width, outWidth);
        (int[] yStart, float[][] yWeights) = AreaWeights(height, outHeight);

        // Horizontal pass: height x outWidth.
        var tmp = new double[height * outWidth];
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int ox = 0; ox < outWidth; ox++)
            {
                double sum = 0;
                float[] w = xWeights[ox];
                int start = xStart[ox];
                for (int k = 0; k < w.Length; k++)
                {
                    sum += w[k] * src[row + start + k];
                }

                tmp[(y * outWidth) + ox] = sum;
            }
        }

        // Vertical pass: outHeight x outWidth.
        var dst = new float[outWidth * outHeight];
        for (int oy = 0; oy < outHeight; oy++)
        {
            float[] w = yWeights[oy];
            int start = yStart[oy];
            for (int ox = 0; ox < outWidth; ox++)
            {
                double sum = 0;
                for (int k = 0; k < w.Length; k++)
                {
                    sum += w[k] * tmp[((start + k) * outWidth) + ox];
                }

                dst[(oy * outWidth) + ox] = (float)sum;
            }
        }

        return dst;
    }

    private static (int[] Start, float[][] Weights) AreaWeights(int nIn, int nOut)
    {
        var starts = new int[nOut];
        var weights = new float[nOut][];
        double scale = (double)nIn / nOut;
        for (int i = 0; i < nOut; i++)
        {
            double start = i * scale;
            double end = (i + 1) * scale;
            int j0 = (int)Math.Floor(start);
            int j1 = Math.Min((int)Math.Ceiling(end), nIn);
            var w = new double[Math.Max(j1 - j0, 1)];
            double total = 0;
            for (int j = j0; j < j1; j++)
            {
                double overlap = Math.Min(end, j + 1) - Math.Max(start, j);
                if (overlap > 0)
                {
                    w[j - j0] = overlap;
                    total += overlap;
                }
            }

            starts[i] = Math.Min(j0, nIn - 1);
            weights[i] = new float[w.Length];
            for (int k = 0; k < w.Length; k++)
            {
                weights[i][k] = (float)(total > 0 ? w[k] / total : (k == 0 ? 1 : 0));
            }
        }

        return (starts, weights);
    }

    /// <summary>
    /// Box-filter mean over a (2r+1) x (2r+1) window with edge replication ("nearest" padding): every
    /// window has the full size, and out-of-bounds taps repeat the nearest edge pixel. Separable, O(1)
    /// per pixel regardless of radius.
    /// </summary>
    public static float[] BoxMean(float[] src, int width, int height, int radius)
    {
        if (radius <= 0)
        {
            return (float[])src.Clone();
        }

        int size = (2 * radius) + 1;
        var tmp = new float[src.Length];
        var line = new double[Math.Max(width, height) + (2 * radius)];

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int i = 0; i < width + (2 * radius); i++)
            {
                line[i] = src[row + Math.Clamp(i - radius, 0, width - 1)];
            }

            double sum = 0;
            for (int i = 0; i < size; i++)
            {
                sum += line[i];
            }

            for (int x = 0; x < width; x++)
            {
                tmp[row + x] = (float)(sum / size);
                sum += line[x + size < line.Length ? x + size : line.Length - 1] - line[x];
            }
        }

        var dst = new float[src.Length];
        for (int x = 0; x < width; x++)
        {
            for (int i = 0; i < height + (2 * radius); i++)
            {
                line[i] = tmp[(Math.Clamp(i - radius, 0, height - 1) * width) + x];
            }

            double sum = 0;
            for (int i = 0; i < size; i++)
            {
                sum += line[i];
            }

            for (int y = 0; y < height; y++)
            {
                dst[(y * width) + x] = (float)(sum / size);
                sum += line[y + size < line.Length ? y + size : line.Length - 1] - line[y];
            }
        }

        return dst;
    }

    /// <summary>Bilinear resize with pixel-centre alignment and clamped edges (OpenCV INTER_LINEAR semantics).</summary>
    public static float[] ResizeBilinear(float[] src, int width, int height, int outWidth, int outHeight)
    {
        (int[] x0, float[] fx) = LinearTaps(width, outWidth);
        (int[] y0, float[] fy) = LinearTaps(height, outHeight);
        var dst = new float[outWidth * outHeight];
        for (int oy = 0; oy < outHeight; oy++)
        {
            int ya = y0[oy];
            int yb = Math.Min(ya + 1, height - 1);
            float wy = fy[oy];
            for (int ox = 0; ox < outWidth; ox++)
            {
                int xa = x0[ox];
                int xb = Math.Min(xa + 1, width - 1);
                float wx = fx[ox];
                float top = src[(ya * width) + xa] + ((src[(ya * width) + xb] - src[(ya * width) + xa]) * wx);
                float bottom = src[(yb * width) + xa] + ((src[(yb * width) + xb] - src[(yb * width) + xa]) * wx);
                dst[(oy * outWidth) + ox] = top + ((bottom - top) * wy);
            }
        }

        return dst;
    }

    private static (int[] Index, float[] Frac) LinearTaps(int nIn, int nOut)
    {
        var index = new int[nOut];
        var frac = new float[nOut];
        double scale = (double)nIn / nOut;
        for (int i = 0; i < nOut; i++)
        {
            double f = ((i + 0.5) * scale) - 0.5;
            int s = (int)Math.Floor(f);
            double t = f - s;
            if (s < 0)
            {
                s = 0;
                t = 0;
            }

            if (s >= nIn - 1)
            {
                s = nIn - 1;
                t = 0;
            }

            index[i] = s;
            frac[i] = (float)t;
        }

        return (index, frac);
    }

    /// <summary>Bicubic resize (Keys, a = -0.75), pixel-centre aligned, clamped edges (OpenCV INTER_CUBIC semantics).</summary>
    public static float[] ResizeBicubic(float[] src, int width, int height, int outWidth, int outHeight)
    {
        (int[] xs, float[][] xw) = CubicTaps(width, outWidth);
        (int[] ys, float[][] yw) = CubicTaps(height, outHeight);

        var tmp = new float[height * outWidth];
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int ox = 0; ox < outWidth; ox++)
            {
                float[] w = xw[ox];
                int s = xs[ox];
                float sum = 0;
                for (int k = 0; k < 4; k++)
                {
                    sum += w[k] * src[row + Math.Clamp(s - 1 + k, 0, width - 1)];
                }

                tmp[(y * outWidth) + ox] = sum;
            }
        }

        var dst = new float[outWidth * outHeight];
        for (int oy = 0; oy < outHeight; oy++)
        {
            float[] w = yw[oy];
            int s = ys[oy];
            for (int ox = 0; ox < outWidth; ox++)
            {
                float sum = 0;
                for (int k = 0; k < 4; k++)
                {
                    sum += w[k] * tmp[(Math.Clamp(s - 1 + k, 0, height - 1) * outWidth) + ox];
                }

                dst[(oy * outWidth) + ox] = sum;
            }
        }

        return dst;
    }

    private static (int[] Start, float[][] Weights) CubicTaps(int nIn, int nOut)
    {
        const double a = -0.75;
        var start = new int[nOut];
        var weights = new float[nOut][];
        double scale = (double)nIn / nOut;
        for (int i = 0; i < nOut; i++)
        {
            double f = ((i + 0.5) * scale) - 0.5;
            int s = (int)Math.Floor(f);
            double t = f - s;
            double[] w =
            {
                ((((a * (t + 1)) - (5 * a)) * (t + 1)) + (8 * a)) * (t + 1) - (4 * a),
                ((((a + 2) * t) - (a + 3)) * t * t) + 1,
                ((((a + 2) * (1 - t)) - (a + 3)) * (1 - t) * (1 - t)) + 1,
                0
            };
            w[3] = 1 - w[0] - w[1] - w[2];
            start[i] = s;
            weights[i] = new[] { (float)w[0], (float)w[1], (float)w[2], (float)w[3] };
        }

        return (start, weights);
    }

    /// <summary>Separable Gaussian blur with reflect-101 borders (OpenCV's default border mode).</summary>
    public static float[] GaussianBlur(float[] src, int width, int height, double sigma)
    {
        int radius = Math.Max(1, (int)Math.Round(sigma * 4)); // OpenCV: ksize = round(sigma*8+1) | 1 for float input
        var kernel = new float[(2 * radius) + 1];
        double total = 0;
        for (int i = -radius; i <= radius; i++)
        {
            double v = Math.Exp(-(i * i) / (2 * sigma * sigma));
            kernel[i + radius] = (float)v;
            total += v;
        }

        for (int i = 0; i < kernel.Length; i++)
        {
            kernel[i] = (float)(kernel[i] / total);
        }

        var tmp = new float[src.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float sum = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    sum += kernel[k + radius] * src[(y * width) + Reflect101(x + k, width)];
                }

                tmp[(y * width) + x] = sum;
            }
        }

        var dst = new float[src.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float sum = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    sum += kernel[k + radius] * tmp[(Reflect101(y + k, height) * width) + x];
                }

                dst[(y * width) + x] = sum;
            }
        }

        return dst;
    }

    private static int Reflect101(int i, int n)
    {
        if (n == 1)
        {
            return 0;
        }

        while (i < 0 || i >= n)
        {
            i = i < 0 ? -i : (2 * n) - 2 - i;
        }

        return i;
    }

    /// <summary>Binary dilation with a (2r+1) x (2r+1) square (separable max filter).</summary>
    public static bool[] DilateSquare(bool[] src, int width, int height, int radius)
    {
        if (radius <= 0)
        {
            return (bool[])src.Clone();
        }

        var tmp = new bool[src.Length];
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            int lastOn = int.MinValue / 2;
            // Forward sweep marks pixels within `radius` after an on pixel; backward sweep handles before.
            for (int x = 0; x < width; x++)
            {
                if (src[row + x])
                {
                    lastOn = x;
                }

                tmp[row + x] = x - lastOn <= radius;
            }

            lastOn = int.MaxValue / 2;
            for (int x = width - 1; x >= 0; x--)
            {
                if (src[row + x])
                {
                    lastOn = x;
                }

                tmp[row + x] |= lastOn - x <= radius;
            }
        }

        var dst = new bool[src.Length];
        for (int x = 0; x < width; x++)
        {
            int lastOn = int.MinValue / 2;
            for (int y = 0; y < height; y++)
            {
                if (tmp[(y * width) + x])
                {
                    lastOn = y;
                }

                dst[(y * width) + x] = y - lastOn <= radius;
            }

            lastOn = int.MaxValue / 2;
            for (int y = height - 1; y >= 0; y--)
            {
                if (tmp[(y * width) + x])
                {
                    lastOn = y;
                }

                dst[(y * width) + x] |= lastOn - y <= radius;
            }
        }

        return dst;
    }
}
