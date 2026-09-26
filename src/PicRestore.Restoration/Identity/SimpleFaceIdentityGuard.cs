using PicRestore.Core.Abstractions;
using PicRestore.Core.Imaging;
using PicRestore.Core.Models;

namespace PicRestore.Restoration.Identity;

/// <summary>
/// Phase-1 placeholder for the design plan's identity-preservation check. Without a trained
/// facial-landmark model (a Phase-2 roadmap item), this guard uses a conservative proxy: it flags a
/// reconstruction when a single contiguous damaged region is large enough that "plausible completion"
/// would mean guessing most of a feature rather than continuing a small gap - in line with "restore
/// less rather than invent".
///
/// This is intentionally cautious rather than precise: it will flag some reconstructions that would
/// have turned out fine. Callers should treat a failing result as "fall back to a smaller, more
/// conservative repair", never as a confirmed identity change.
/// </summary>
public sealed class SimpleFaceIdentityGuard : IFaceIdentityGuard
{
    private static readonly (int Dx, int Dy)[] Neighbours4 = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    // A contiguous repaired blob larger than this share of the image is treated as too large to
    // complete with confidence.
    private const float MaxConfidentBlobShare = 0.06f;

    public IdentityCheckResult Validate(RasterImage original, RasterImage candidate, DamageMask mask)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(mask);

        int totalPixels = mask.Width * mask.Height;
        if (totalPixels == 0)
        {
            return IdentityCheckResult.Ok();
        }

        int largestBlob = FindLargestContiguousRepairedBlob(mask);
        float share = (float)largestBlob / totalPixels;

        if (share <= MaxConfidentBlobShare)
        {
            return new IdentityCheckResult(
                true, share, "Largest repaired region is small enough to complete with confidence.");
        }

        return new IdentityCheckResult(
            false,
            share,
            $"Largest contiguous repaired region covers {share:P1} of the image - too large to " +
            "reconstruct with confidence; falling back to a more conservative repair.");
    }

    private static int FindLargestContiguousRepairedBlob(DamageMask mask)
    {
        var visited = new bool[mask.Width * mask.Height];
        int largest = 0;
        var stack = new Stack<(int X, int Y)>();

        for (int y = 0; y < mask.Height; y++)
        {
            for (int x = 0; x < mask.Width; x++)
            {
                int start = mask.IndexOf(x, y);
                if (visited[start] || !mask.IsRepairable(x, y, 0.5f))
                {
                    continue;
                }

                int size = 0;
                stack.Push((x, y));
                visited[start] = true;

                while (stack.Count > 0)
                {
                    (int cx, int cy) = stack.Pop();
                    size++;

                    foreach ((int dx, int dy) in Neighbours4)
                    {
                        int nx = cx + dx;
                        int ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= mask.Width || ny >= mask.Height)
                        {
                            continue;
                        }

                        int ni = mask.IndexOf(nx, ny);
                        if (!visited[ni] && mask.IsRepairable(nx, ny, 0.5f))
                        {
                            visited[ni] = true;
                            stack.Push((nx, ny));
                        }
                    }
                }

                largest = Math.Max(largest, size);
            }
        }

        return largest;
    }
}
