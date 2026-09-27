using PicRestore.Core.Imaging;
using PicRestore.Core.Models;

namespace PicRestore.Core.Abstractions;

/// <summary>
/// Final, deliberately slight clarity pass that compensates for age-related blur and chemical softening
/// (the restoration protocol's "controlled sharpening", ~10%). Must never touch a protected pixel and
/// must not create halos, plastic skin or artificial micro-detail.
/// </summary>
public interface ISharpener
{
    RasterImage Sharpen(RasterImage image, DamageMask mask, ProcessingSettings settings);
}
