using PicRestore.Core.Abstractions;
using PicRestore.Core.Imaging;
using PicRestore.Core.Models;

namespace PicRestore.Restoration.Inpainting;

/// <summary>
/// Classical harmonic (Laplace-equation) diffusion inpainting: the same family of PDE-based methods as
/// OpenCV's Navier-Stokes/Telea inpainters, reimplemented in pure managed code so the Phase-1 pipeline
/// has no native dependency. Each damaged pixel is repeatedly averaged with its neighbours until the
/// hole is filled by information propagating in from its boundary - it continues nearby structure
/// smoothly, but (deliberately) cannot invent texture or detail the surroundings don't already imply.
///
/// This is the conservative, always-available reconstruction path described in the design plan; the
/// roadmap's Phase 2 adds a structure-aware learned model (LaMa) for larger or more structured damage,
/// behind the same <see cref="IInpainter"/> abstraction.
/// </summary>
public sealed class DiffusionInpainter : IInpainter
{
    private const int MaxIterations = 250;

    private static readonly (int Dx, int Dy)[] Neighbours8 =
    {
        (-1, -1), (0, -1), (1, -1),
        (-1, 0), (1, 0),
        (-1, 1), (0, 1), (1, 1)
    };

    public RasterImage Inpaint(RasterImage image, DamageMask mask, ProcessingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(settings);

        float strength = Math.Clamp(settings.ReconstructionStrength, 0f, 1f);
        int width = image.Width;
        int height = image.Height;

        List<(int X, int Y)> repairable = CollectRepairablePixels(mask, settings.RepairThreshold);
        if (repairable.Count == 0 || strength <= 0f)
        {
            return image.Clone();
        }

        var working = (float[])image.Pixels.Clone();

        // Seed each hole with the average colour of its immediate known boundary, so diffusion starts
        // from a sensible value instead of from whatever the damaged pixel currently holds (often
        // stark white, which would otherwise bias early iterations).
        SeedFromBoundary(working, width, height, mask, repairable, settings.RepairThreshold);

        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            foreach ((int x, int y) in repairable)
            {
                int i = ((y * width) + x) * 4;
                for (int channel = 0; channel < 3; channel++)
                {
                    float left = Sample(working, width, height, x - 1, y, channel);
                    float right = Sample(working, width, height, x + 1, y, channel);
                    float up = Sample(working, width, height, x, y - 1, channel);
                    float down = Sample(working, width, height, x, y + 1, channel);
                    working[i + channel] = (left + right + up + down) / 4f;
                }
            }
        }

        var output = new RasterImage(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                (float r, float g, float b, float a) = image.GetPixel(x, y);
                if (!mask.IsReconstructable(x, y, settings.RepairThreshold))
                {
                    output.SetPixel(x, y, r, g, b, a);
                    continue;
                }

                int i = ((y * width) + x) * 4;
                float outR = Lerp(r, working[i], strength);
                float outG = Lerp(g, working[i + 1], strength);
                float outB = Lerp(b, working[i + 2], strength);
                output.SetPixel(x, y, outR, outG, outB, a);
            }
        }

        return output;
    }

    private static List<(int X, int Y)> CollectRepairablePixels(DamageMask mask, float threshold)
    {
        var pixels = new List<(int, int)>();
        for (int y = 0; y < mask.Height; y++)
        {
            for (int x = 0; x < mask.Width; x++)
            {
                if (mask.IsReconstructable(x, y, threshold))
                {
                    pixels.Add((x, y));
                }
            }
        }

        return pixels;
    }

    private static void SeedFromBoundary(
        float[] working, int width, int height, DamageMask mask, List<(int X, int Y)> repairable, float threshold)
    {
        foreach ((int x, int y) in repairable)
        {
            double sumR = 0, sumG = 0, sumB = 0;
            int count = 0;

            foreach ((int dx, int dy) in Neighbours8)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                {
                    continue;
                }

                if (mask.IsReconstructable(nx, ny, threshold))
                {
                    continue; // Only seed from pixels that are already known.
                }

                int ni = ((ny * width) + nx) * 4;
                sumR += working[ni];
                sumG += working[ni + 1];
                sumB += working[ni + 2];
                count++;
            }

            if (count == 0)
            {
                continue; // Fully surrounded by damage - the diffusion loop still reaches it eventually.
            }

            int i = ((y * width) + x) * 4;
            working[i] = (float)(sumR / count);
            working[i + 1] = (float)(sumG / count);
            working[i + 2] = (float)(sumB / count);
        }
    }

    private static float Sample(float[] pixels, int width, int height, int x, int y, int channel)
    {
        x = Math.Clamp(x, 0, width - 1);
        y = Math.Clamp(y, 0, height - 1);
        return pixels[(((y * width) + x) * 4) + channel];
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);
}
