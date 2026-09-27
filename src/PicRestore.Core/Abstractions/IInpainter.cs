using PicRestore.Core.Imaging;
using PicRestore.Core.Models;

namespace PicRestore.Core.Abstractions;

/// <summary>
/// Reconstructs pixels inside the damage mask from the surrounding, undamaged context. Implementations
/// must never change intact content beyond a narrow seam-blending margin around the mask (a few pixels,
/// so a repair doesn't leave a hard edge or a halo of the damage's own rim), and must never touch a
/// protected ("never touch") pixel.
/// </summary>
public interface IInpainter
{
    RasterImage Inpaint(RasterImage image, DamageMask mask, ProcessingSettings settings);
}
