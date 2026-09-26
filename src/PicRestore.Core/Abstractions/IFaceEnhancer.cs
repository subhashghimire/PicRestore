using PicRestore.Core.Imaging;
using PicRestore.Core.Models;

namespace PicRestore.Core.Abstractions;

/// <summary>
/// Optional, opt-in generative face enhancement (GFPGAN/CodeFormer/GPEN-style). This sits outside the
/// default restoration path entirely: the pipeline only calls it when
/// <see cref="ProcessingSettings.EnableAiEnhance"/> is explicitly true for that project.
/// </summary>
public interface IFaceEnhancer
{
    RasterImage Enhance(RasterImage image, ProcessingSettings settings);
}
