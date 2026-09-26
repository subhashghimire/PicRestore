using PicRestore.Core.Abstractions;
using PicRestore.Core.Imaging;
using PicRestore.Core.Models;

namespace PicRestore.Restoration.Enhancement;

/// <summary>
/// Phase-2/3 target for the opt-in "AI enhance" mode (GFPGAN/CodeFormer/GPEN-style, run through
/// ONNX Runtime + DirectML per the technology stack). Wiring in a real exported model is tracked on
/// the roadmap; until then this throws rather than silently doing nothing, so a misconfigured
/// "enabled" project fails loudly instead of shipping an untouched image labelled as enhanced.
///
/// The pipeline only ever calls this when <see cref="ProcessingSettings.EnableAiEnhance"/> is
/// explicitly true for that project - it is never part of the default restoration path.
/// </summary>
public sealed class OnnxFaceEnhancer : IFaceEnhancer
{
    public RasterImage Enhance(RasterImage image, ProcessingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.EnableAiEnhance)
        {
            throw new InvalidOperationException(
                "OnnxFaceEnhancer.Enhance was called without EnableAiEnhance set - the AI-enhance path " +
                "must never run unless the user opted in for this project.");
        }

        throw new NotSupportedException(
            "AI-enhance mode is not yet wired to a model in this build. Plug a GFPGAN/CodeFormer/GPEN " +
            "ONNX export in here (see the design plan's Technology Stack section) before enabling this path.");
    }
}
