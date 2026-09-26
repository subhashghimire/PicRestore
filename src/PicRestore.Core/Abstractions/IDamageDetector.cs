using PicRestore.Core.Imaging;
using PicRestore.Core.Models;

namespace PicRestore.Core.Abstractions;

/// <summary>
/// Proposes a damage mask for a photo. Implementations should be biased toward under-detection: a
/// missed scratch can be brushed in by the user, but a false positive on an intact area risks that
/// area being repainted.
/// </summary>
public interface IDamageDetector
{
    DamageMask Detect(RasterImage image, ProcessingSettings settings);
}
