using PicRestore.Core.Abstractions;
using PicRestore.Core.Imaging;
using PicRestore.Core.Models;
using PicRestore.Restoration.Imaging;

namespace PicRestore.Restoration.Identity;

/// <summary>
/// Face detection with YuNet (OpenCV Zoo "face_detection_yunet_2023mar", MIT), decoded here in plain C#;
/// the network itself is run by whatever <see cref="RunTile"/> delegate the host supplies (ONNX Runtime in
/// the app). The photo is searched at two scales - whole frame at 640 px, and 640 px tiles of a 1280 px
/// version - so both close-ups and small faces in group photos are found; overlapping hits are merged with
/// non-maximum suppression. On five family photos it found every face (plus one ear).
/// </summary>
public sealed class YuNetFaceDetector : IFaceDetector
{
    public const int InputSize = 640;
    public const float ScoreThreshold = 0.6f;
    public const float NmsThreshold = 0.3f;
    private static readonly int[] Strides = { 8, 16, 32 };

    /// <summary>
    /// Runs the network on one 640x640 tile given as planar B,G,R with raw 0..255 values; returns the
    /// named outputs (cls_8, obj_8, bbox_8, ... cls_32, obj_32, bbox_32) as flat arrays.
    /// </summary>
    public Func<float[], IReadOnlyDictionary<string, float[]>> RunTile { get; }

    public YuNetFaceDetector(Func<float[], IReadOnlyDictionary<string, float[]>> runTile)
    {
        RunTile = runTile ?? throw new ArgumentNullException(nameof(runTile));
    }

    public IReadOnlyList<FaceRegion> Detect(RasterImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        int width = image.Width, height = image.Height, n = width * height;
        var planes = new float[3][];
        for (int c = 0; c < 3; c++)
        {
            planes[c] = new float[n];
            for (int i = 0; i < n; i++)
            {
                planes[c][i] = image.Pixels[(i * 4) + c] * 255f;
            }
        }

        var found = new List<FaceRegion>();
        foreach (int longSide in new[] { InputSize, 2 * InputSize })
        {
            double scale = (double)longSide / Math.Max(width, height);
            int sw = Math.Max(1, (int)Math.Round(width * scale)), sh = Math.Max(1, (int)Math.Round(height * scale));
            float[][] small = planes.Select(p => Resize(p, width, height, sw, sh)).ToArray();
            foreach (int ty in Starts(sh))
            {
                foreach (int tx in Starts(sw))
                {
                    float[] tile = new float[3 * InputSize * InputSize];
                    for (int y = 0; y < InputSize && ty + y < sh; y++)
                    {
                        for (int x = 0; x < InputSize && tx + x < sw; x++)
                        {
                            int s = ((ty + y) * sw) + tx + x, t = (y * InputSize) + x;
                            tile[t] = small[2][s];                                // B
                            tile[(InputSize * InputSize) + t] = small[1][s];      // G
                            tile[(2 * InputSize * InputSize) + t] = small[0][s];  // R
                        }
                    }

                    foreach (FaceRegion f in Decode(RunTile(tile)))
                    {
                        found.Add(new FaceRegion(
                            (float)((f.X + tx) / scale), (float)((f.Y + ty) / scale),
                            (float)(f.Width / scale), (float)(f.Height / scale), f.Score));
                    }
                }
            }
        }

        return Suppress(found);
    }

    /// <summary>Decodes one tile's raw outputs into boxes in tile pixel coordinates.</summary>
    public static List<FaceRegion> Decode(IReadOnlyDictionary<string, float[]> outputs)
    {
        var boxes = new List<FaceRegion>();
        foreach (int stride in Strides)
        {
            int cells = InputSize / stride;
            float[] cls = outputs[$"cls_{stride}"], obj = outputs[$"obj_{stride}"], bbox = outputs[$"bbox_{stride}"];
            for (int idx = 0; idx < cells * cells; idx++)
            {
                float score = MathF.Sqrt(Math.Clamp(cls[idx], 0f, 1f) * Math.Clamp(obj[idx], 0f, 1f));
                if (score <= ScoreThreshold)
                {
                    continue;
                }

                int row = idx / cells, col = idx % cells;
                float cx = (col + bbox[idx * 4]) * stride, cy = (row + bbox[(idx * 4) + 1]) * stride;
                float w = MathF.Exp(bbox[(idx * 4) + 2]) * stride, h = MathF.Exp(bbox[(idx * 4) + 3]) * stride;
                boxes.Add(new FaceRegion(cx - (w / 2), cy - (h / 2), w, h, score));
            }
        }

        return boxes;
    }

    private static List<FaceRegion> Suppress(List<FaceRegion> boxes)
    {
        var keep = new List<FaceRegion>();
        foreach (FaceRegion b in boxes.OrderByDescending(b => b.Score))
        {
            if (keep.All(k => IoU(b, k) <= NmsThreshold))
            {
                keep.Add(b);
            }
        }

        return keep;
    }

    private static float IoU(FaceRegion a, FaceRegion b)
    {
        float ix = MathF.Max(0, MathF.Min(a.X + a.Width, b.X + b.Width) - MathF.Max(a.X, b.X));
        float iy = MathF.Max(0, MathF.Min(a.Y + a.Height, b.Y + b.Height) - MathF.Max(a.Y, b.Y));
        float inter = ix * iy;
        return inter / ((a.Width * a.Height) + (b.Width * b.Height) - inter);
    }

    private static IEnumerable<int> Starts(int length)
    {
        int last = Math.Max(length - InputSize, 0);
        var starts = new List<int>();
        for (int s = 0; s <= last; s += InputSize - 128)
        {
            starts.Add(s);
        }

        if (starts[^1] != last)
        {
            starts.Add(last);
        }

        return starts;
    }

    private static float[] Resize(float[] plane, int w, int h, int ow, int oh) =>
        ow <= w && oh <= h ? ImageOps.ResizeArea(plane, w, h, ow, oh) : ImageOps.ResizeBilinear(plane, w, h, ow, oh);
}
