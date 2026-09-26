using PicRestore.Core.Imaging;
using PicRestore.Core.Models;

namespace PicRestore.Core.Abstractions;

/// <summary>
/// Corrects age-related colour and tone shifts (yellowing, bleaching, uneven fading, contrast loss)
/// using the photo's own surviving pixels as the reference. Never introduces colour the surviving
/// pixels don't support.
/// </summary>
public interface IColorRestorer
{
    RasterImage Restore(RasterImage image, DamageMask mask, ProcessingSettings settings);
}
