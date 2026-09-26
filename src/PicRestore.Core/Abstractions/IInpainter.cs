using PicRestore.Core.Imaging;
using PicRestore.Core.Models;

namespace PicRestore.Core.Abstractions;

/// <summary>
/// Reconstructs pixels inside the damage mask from the surrounding, undamaged context. Implementations
/// must never write outside the mask, and must never touch a protected ("never touch") pixel.
/// </summary>
public interface IInpainter
{
    RasterImage Inpaint(RasterImage image, DamageMask mask, ProcessingSettings settings);
}
