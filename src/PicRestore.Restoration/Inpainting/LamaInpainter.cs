using PicRestore.Core.Abstractions;
using PicRestore.Core.Imaging;
using PicRestore.Core.Models;
using PicRestore.Restoration.Imaging;

namespace PicRestore.Restoration.Inpainting;

/// <summary>
/// Structure-aware reconstruction with a learned inpainting network (LaMa - "Resolution-robust Large
/// Mask Inpainting with Fourier Convolutions", Apache-2.0), the design plan's Phase-2 reconstruction
/// path behind the same <see cref="IInpainter"/> abstraction as the classical diffusion fill.
///
/// <para>
/// How it runs: the photo and its reconstructable mask are scaled to a working resolution (long side
/// <see cref="WorkingLongSide"/>), covered with overlapping <see cref="IInpaintingModel.TileSize"/>
/// tiles, and each tile that still contains unfilled damage is sent through the network. Tiles are
/// processed in reading order and write back only the pixels they filled, so a later tile sees - and
/// continues from - what earlier tiles reconstructed. The filled working image is upscaled and
/// composited into the full-resolution photo only inside the mask (with a small feathered seam), so
/// undamaged pixels keep their original full-resolution detail.
/// </para>
///
/// <para>
/// If no model is available (not downloaded yet, offline, failed to load) this falls back to
/// <see cref="DiffusionInpainter"/> and says so in <see cref="LastRunNote"/>, so the restoration still
/// completes and the audit trail is honest about which method ran.
/// </para>
/// </summary>
public sealed class LamaInpainter : IInpainter, IStageDiagnostics
{
    /// <summary>Long side of the working image. Measured: 1536 beat both 1280 and 2048 on the reference photo.</summary>
    public const int WorkingLongSide = 1536;

    /// <summary>Overlap between neighbouring tiles, in working-resolution pixels.</summary>
    public const int TileOverlap = 128;

    /// <summary>
    /// Full-resolution dilation of the mask before filling, so the bright/dark rim that damage leaves
    /// around its detected core is replaced too instead of being copied into the fill as "context".
    /// </summary>
    public const int MaskDilationRadius = 6;

    /// <summary>
    /// "Locked elements" (restoration protocol, checklist step 2): the dilation above and the seam
    /// feather may only extend into pixels with at least this much damage evidence. Pixels the detector
    /// is confident are intact - faces, fabric stripes, prints, anything undamaged - are locked and never
    /// changed, even right next to a repair.
    /// </summary>
    public const float LockedBelowProbability = 0.15f;

    private readonly IInpainter _fallback;

    public LamaInpainter(IInpaintingModel? model = null, IInpainter? fallback = null)
    {
        Model = model;
        _fallback = fallback ?? new DiffusionInpainter();
    }

    /// <summary>The network to use; null means "use the classical fallback".</summary>
    public IInpaintingModel? Model { get; set; }

    /// <summary>Why <see cref="Model"/> is null, if known (shown in the audit trail when falling back).</summary>
    public string? ModelUnavailableReason { get; set; }

    public Action<string>? StatusCallback { get; set; }

    public string? LastRunNote { get; private set; }

    public RasterImage Inpaint(RasterImage image, DamageMask mask, ProcessingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(settings);

        float strength = Math.Clamp(settings.ReconstructionStrength, 0f, 1f);
        int width = image.Width;
        int height = image.Height;
        int n = width * height;

        var holes = new bool[n];
        bool any = false;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (mask.IsReconstructable(x, y, settings.RepairThreshold))
                {
                    holes[(y * width) + x] = true;
                    any = true;
                }
            }
        }

        if (!any || strength <= 0f)
        {
            LastRunNote = any ? "Reconstruction strength is 0 - nothing changed." : "No reconstructable damage in the mask.";
            return image.Clone();
        }

        IInpaintingModel? model = Model;
        if (model is null)
        {
            LastRunNote = "AI reconstruction model unavailable" +
                (ModelUnavailableReason is null ? "" : $" ({ModelUnavailableReason})") +
                " - used the basic diffusion fill instead, which smooths damage rather than rebuilding texture.";
            return _fallback.Inpaint(image, mask, settings);
        }

        try
        {
            RasterImage result = RunModel(model, image, mask, holes, strength);
            LastRunNote = "Reconstructed with the LaMa AI inpainting model.";
            return result;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LastRunNote = $"AI reconstruction failed ({ex.Message}) - used the basic diffusion fill instead.";
            return _fallback.Inpaint(image, mask, settings);
        }
    }

    private RasterImage RunModel(IInpaintingModel model, RasterImage image, DamageMask mask, bool[] holes, float strength)
    {
        int width = image.Width;
        int height = image.Height;
        int n = width * height;
        int tile = model.TileSize;

        bool[] dilated = ImageOps.DilateSquare(holes, width, height, MaskDilationRadius);
        var locked = new bool[n];
        for (int i = 0; i < n; i++)
        {
            // Never fill (or blend into) a pixel the user protected, nor grow the repair into a pixel the
            // detector is confident is intact ground truth.
            locked[i] = mask.Protected[i] || (!holes[i] && mask.Probability[i] < LockedBelowProbability);
            if (locked[i])
            {
                dilated[i] = false;
            }
        }

        double scale = Math.Min(1.0, (double)WorkingLongSide / Math.Max(width, height));
        int workHeight = Math.Max(tile, (int)Math.Round(height * scale));
        int workWidth = Math.Max(tile, (int)Math.Round(width * scale));

        // Planar working copies (R, G, B) and the working-resolution mask.
        var planes = new float[3][];
        var maskF = new float[n];
        for (int i = 0; i < n; i++)
        {
            maskF[i] = dilated[i] ? 1f : 0f;
        }

        for (int c = 0; c < 3; c++)
        {
            var plane = new float[n];
            for (int i = 0; i < n; i++)
            {
                plane[i] = image.Pixels[(i * 4) + c];
            }

            planes[c] = Resample(plane, width, height, workWidth, workHeight);
        }

        float[] workMaskF = Resample(maskF, width, height, workWidth, workHeight);
        var remaining = new bool[workWidth * workHeight];
        for (int i = 0; i < remaining.Length; i++)
        {
            remaining[i] = workMaskF[i] > 0.01f;
        }

        List<int> ys = TileStarts(workHeight, tile);
        List<int> xs = TileStarts(workWidth, tile);
        var jobs = new List<(int X, int Y)>();
        foreach (int ty in ys)
        {
            foreach (int tx in xs)
            {
                jobs.Add((tx, ty));
            }
        }

        int total = jobs.Count(j => AnyRemaining(remaining, workWidth, j.X, j.Y, tile));
        int done = 0;
        var inputBuffer = new float[3 * tile * tile];
        var maskBuffer = new float[tile * tile];
        var outputBuffer = new float[3 * tile * tile];

        foreach ((int tx, int ty) in jobs)
        {
            if (!AnyRemaining(remaining, workWidth, tx, ty, tile))
            {
                continue;
            }

            done++;
            StatusCallback?.Invoke($"Reconstructing damaged areas (AI) - part {done} of {Math.Max(total, done)}");

            for (int y = 0; y < tile; y++)
            {
                for (int x = 0; x < tile; x++)
                {
                    int w = ((ty + y) * workWidth) + tx + x;
                    int t = (y * tile) + x;
                    inputBuffer[t] = planes[2][w];                     // B
                    inputBuffer[(tile * tile) + t] = planes[1][w];     // G
                    inputBuffer[(2 * tile * tile) + t] = planes[0][w]; // R
                    maskBuffer[t] = remaining[w] ? 1f : 0f;
                }
            }

            model.Run(inputBuffer, maskBuffer, outputBuffer);

            for (int y = 0; y < tile; y++)
            {
                for (int x = 0; x < tile; x++)
                {
                    int w = ((ty + y) * workWidth) + tx + x;
                    if (!remaining[w])
                    {
                        continue;
                    }

                    int t = (y * tile) + x;
                    planes[2][w] = Math.Clamp(outputBuffer[t] / 255f, 0f, 1f);
                    planes[1][w] = Math.Clamp(outputBuffer[(tile * tile) + t] / 255f, 0f, 1f);
                    planes[0][w] = Math.Clamp(outputBuffer[(2 * tile * tile) + t] / 255f, 0f, 1f);
                    remaining[w] = false;
                }
            }
        }

        // Feathered composite: full weight inside the (dilated) mask, a soft ~3 px seam just outside it.
        float[] alpha = ImageOps.GaussianBlur(maskF, width, height, 1.5);
        for (int i = 0; i < n; i++)
        {
            alpha[i] = locked[i] ? 0f : MathF.Max(alpha[i], maskF[i]) * strength;
        }

        var output = image.Clone();
        for (int c = 0; c < 3; c++)
        {
            float[] up = workWidth == width && workHeight == height
                ? planes[c]
                : ImageOps.ResizeBicubic(planes[c], workWidth, workHeight, width, height);
            for (int i = 0; i < n; i++)
            {
                float a = alpha[i];
                if (a <= 0f)
                {
                    continue;
                }

                float original = image.Pixels[(i * 4) + c];
                output.Pixels[(i * 4) + c] = Math.Clamp(original + ((Math.Clamp(up[i], 0f, 1f) - original) * a), 0f, 1f);
            }
        }

        return output;
    }

    private static float[] Resample(float[] plane, int width, int height, int outWidth, int outHeight)
    {
        if (outWidth == width && outHeight == height)
        {
            return (float[])plane.Clone();
        }

        return outWidth <= width && outHeight <= height
            ? ImageOps.ResizeArea(plane, width, height, outWidth, outHeight)
            : ImageOps.ResizeBilinear(plane, width, height, outWidth, outHeight);
    }

    private static List<int> TileStarts(int length, int tile)
    {
        int stride = tile - TileOverlap;
        var starts = new List<int>();
        for (int s = 0; s <= Math.Max(length - tile, 0); s += stride)
        {
            starts.Add(s);
        }

        if (starts[^1] != length - tile)
        {
            starts.Add(length - tile);
        }

        return starts;
    }

    private static bool AnyRemaining(bool[] remaining, int workWidth, int tx, int ty, int tile)
    {
        for (int y = 0; y < tile; y++)
        {
            int row = ((ty + y) * workWidth) + tx;
            for (int x = 0; x < tile; x++)
            {
                if (remaining[row + x])
                {
                    return true;
                }
            }
        }

        return false;
    }
}
