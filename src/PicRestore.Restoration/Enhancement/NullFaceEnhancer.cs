using PicRestore.Core.Abstractions;
using PicRestore.Core.Imaging;
using PicRestore.Core.Models;

namespace PicRestore.Restoration.Enhancement;

/// <summary>
/// The default face enhancer: a no-op. Used whenever <see cref="ProcessingSettings.EnableAiEnhance"/>
/// is off, which is the default for every new project.
/// </summary>
public sealed class NullFaceEnhancer : IFaceEnhancer
{
    public RasterImage Enhance(RasterImage image, ProcessingSettings settings) => image;
}
