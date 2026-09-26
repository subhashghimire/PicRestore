using PicRestore.Core.Imaging;
using PicRestore.Core.Models;

namespace PicRestore.Core.Abstractions;

/// <summary>
/// Re-synthesises the original photo's film grain/noise characteristics onto reconstructed regions,
/// so a repair doesn't look artificially smooth next to the surrounding grain.
/// </summary>
public interface IGrainMatcher
{
    RasterImage Apply(RasterImage original, RasterImage reconstructed, DamageMask mask, ProcessingSettings settings);
}
