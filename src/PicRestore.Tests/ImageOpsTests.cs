using PicRestore.Restoration.Imaging;
using Xunit;

namespace PicRestore.Tests;

public class ImageOpsTests
{
    [Fact]
    public void BoxMean_OfAConstantImageIsThatConstant()
    {
        float[] src = Enumerable.Repeat(0.3f, 17 * 11).ToArray();
        float[] result = ImageOps.BoxMean(src, 17, 11, 4);
        Assert.True(result.All(v => Math.Abs(v - 0.3f) < 1e-6f));
    }

    [Fact]
    public void BoxMean_MatchesABruteForceWindowWithEdgeReplication()
    {
        var rng = new Random(4);
        int w = 13, h = 9, r = 3;
        float[] src = Enumerable.Range(0, w * h).Select(_ => (float)rng.NextDouble()).ToArray();
        float[] fast = ImageOps.BoxMean(src, w, h, r);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double sum = 0;
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        sum += src[(Math.Clamp(y + dy, 0, h - 1) * w) + Math.Clamp(x + dx, 0, w - 1)];
                    }
                }

                double expected = sum / ((2 * r) + 1) / ((2 * r) + 1);
                Assert.True(Math.Abs(expected - fast[(y * w) + x]) < 1e-5, $"({x},{y}): {expected} vs {fast[(y * w) + x]}");
            }
        }
    }

    [Fact]
    public void ResizeArea_AveragesExactly()
    {
        float[] src = { 0, 1, 2, 3, 4, 5, 6, 7 }; // 4 x 2
        float[] result = ImageOps.ResizeArea(src, 4, 2, 2, 1);
        Assert.Equal((0 + 1 + 4 + 5) / 4.0, result[0], precision: 5);
        Assert.Equal((2 + 3 + 6 + 7) / 4.0, result[1], precision: 5);
    }

    [Fact]
    public void DilateSquare_GrowsByTheRadius()
    {
        var src = new bool[15 * 15];
        src[(7 * 15) + 7] = true;
        bool[] result = ImageOps.DilateSquare(src, 15, 15, 2);
        Assert.Equal(25, result.Count(v => v));
        Assert.True(result[(5 * 15) + 5] && result[(9 * 15) + 9] && !result[(4 * 15) + 7]);
    }
}
