namespace PicRestore.Restoration.DamageDetection;

/// <summary>
/// Fast local mean/variance over a square window, computed once via summed-area tables (integral
/// images) so each pixel's statistics are O(1) to query regardless of window size.
/// </summary>
internal sealed class LocalStatistics
{
    private readonly double[] _sum;
    private readonly double[] _sumSq;
    private readonly int _width;
    private readonly int _height;
    private readonly int _radius;
    private readonly int _stride;

    public LocalStatistics(float[] values, int width, int height, int radius)
    {
        _width = width;
        _height = height;
        _radius = radius;
        _stride = width + 1;

        _sum = new double[_stride * (height + 1)];
        _sumSq = new double[_stride * (height + 1)];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float v = values[(y * width) + x];

                double sum = v
                    + _sum[(y * _stride) + (x + 1)]
                    + _sum[((y + 1) * _stride) + x]
                    - _sum[(y * _stride) + x];

                double sumSq = ((double)v * v)
                    + _sumSq[(y * _stride) + (x + 1)]
                    + _sumSq[((y + 1) * _stride) + x]
                    - _sumSq[(y * _stride) + x];

                _sum[((y + 1) * _stride) + (x + 1)] = sum;
                _sumSq[((y + 1) * _stride) + (x + 1)] = sumSq;
            }
        }
    }

    public float LocalMean(int x, int y)
    {
        (int count, double sum, _) = WindowSums(x, y);
        return count == 0 ? 0f : (float)(sum / count);
    }

    public float LocalVariance(int x, int y)
    {
        (int count, double sum, double sumSq) = WindowSums(x, y);
        if (count == 0)
        {
            return 0f;
        }

        double mean = sum / count;
        double variance = (sumSq / count) - (mean * mean);
        return (float)Math.Max(0, variance);
    }

    private (int Count, double Sum, double SumSq) WindowSums(int x, int y)
    {
        int x0 = Math.Max(0, x - _radius);
        int x1 = Math.Min(_width - 1, x + _radius);
        int y0 = Math.Max(0, y - _radius);
        int y1 = Math.Min(_height - 1, y + _radius);

        double sum = RegionSum(_sum, x0, y0, x1, y1);
        double sumSq = RegionSum(_sumSq, x0, y0, x1, y1);
        int count = (x1 - x0 + 1) * (y1 - y0 + 1);

        return (count, sum, sumSq);
    }

    private double RegionSum(double[] integral, int x0, int y0, int x1, int y1)
    {
        // The integral image is 1-padded, so (x1+1, y1+1) etc. address the inclusive region
        // [x0..x1, y0..y1] using the standard summed-area-table formula.
        double bottomRight = integral[((y1 + 1) * _stride) + (x1 + 1)];
        double bottomLeft = integral[((y1 + 1) * _stride) + x0];
        double topRight = integral[(y0 * _stride) + (x1 + 1)];
        double topLeft = integral[(y0 * _stride) + x0];

        return bottomRight - bottomLeft - topRight + topLeft;
    }
}
