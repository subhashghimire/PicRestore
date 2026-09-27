using PicRestore.Core.Imaging;
using PicRestore.Core.Models;

namespace PicRestore.Core.Abstractions;

/// <summary>
/// Finds faces so the restoration protocol's "locked elements" rule can protect them: intact facial
/// features are never repainted, only confidently detected (or hand-brushed) damage on a face is repaired.
/// </summary>
public interface IFaceDetector
{
    IReadOnlyList<FaceRegion> Detect(RasterImage image);
}
