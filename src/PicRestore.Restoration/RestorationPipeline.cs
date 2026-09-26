using PicRestore.Core.Abstractions;
using PicRestore.Core.Imaging;
using PicRestore.Core.Models;

namespace PicRestore.Restoration;

/// <summary>
/// Runs the full restoration sequence for one photo: colour restoration, reconstruction (guarded by an
/// automatic identity check with a conservative fallback), grain matching, and - only when explicitly
/// opted into for this project - the AI-enhance pass. Every stage's outcome is recorded on the project
/// so the UI's history/confidence view has something real to show, per the design plan's "nothing is a
/// black box" requirement.
/// </summary>
public sealed class RestorationPipeline
{
    private readonly IColorRestorer _colorRestorer;
    private readonly IInpainter _inpainter;
    private readonly IGrainMatcher _grainMatcher;
    private readonly IFaceIdentityGuard _identityGuard;
    private readonly IFaceEnhancer _faceEnhancer;

    public RestorationPipeline(
        IColorRestorer colorRestorer,
        IInpainter inpainter,
        IGrainMatcher grainMatcher,
        IFaceIdentityGuard identityGuard,
        IFaceEnhancer faceEnhancer)
    {
        _colorRestorer = colorRestorer ?? throw new ArgumentNullException(nameof(colorRestorer));
        _inpainter = inpainter ?? throw new ArgumentNullException(nameof(inpainter));
        _grainMatcher = grainMatcher ?? throw new ArgumentNullException(nameof(grainMatcher));
        _identityGuard = identityGuard ?? throw new ArgumentNullException(nameof(identityGuard));
        _faceEnhancer = faceEnhancer ?? throw new ArgumentNullException(nameof(faceEnhancer));
    }

    public RasterImage Run(RasterImage original, DamageMask mask, ProcessingSettings settings, RestorationProject? project = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(settings);

        RasterImage colorCorrected = _colorRestorer.Restore(original, mask, settings);
        project?.RecordStage(new PipelineStageResult(
            "Colour & Tonal Restoration",
            settings.ColorRestorationStrength > 0f,
            $"Strength {settings.ColorRestorationStrength:P0}."));

        RasterImage reconstructed = _inpainter.Inpaint(colorCorrected, mask, settings);
        project?.RecordStage(new PipelineStageResult(
            "Conservative Reconstruction",
            settings.ReconstructionStrength > 0f,
            $"Strength {settings.ReconstructionStrength:P0}; repaired " +
            $"{mask.CoveragePercentage(settings.RepairThreshold):F1}% of pixels."));

        IdentityCheckResult identityCheck = _identityGuard.Validate(original, reconstructed, mask);
        if (identityCheck.Passed)
        {
            project?.RecordStage(new PipelineStageResult("Identity Check", true, identityCheck.Message));
        }
        else
        {
            // Conservative fallback: keep the colour-corrected (pre-reconstruction) image for this run
            // rather than ship a reconstruction the guard doesn't trust. Re-running the inpainter at a
            // lower strength for just the offending region is a natural follow-up, tracked separately -
            // for now this favours "restore less" over "ship something we can't vouch for".
            reconstructed = colorCorrected;
            project?.RecordStage(new PipelineStageResult("Identity Check", false, identityCheck.Message));
        }

        RasterImage grainMatched = _grainMatcher.Apply(original, reconstructed, mask, settings);
        project?.RecordStage(new PipelineStageResult(
            "Grain & Texture Matching",
            settings.MatchOriginalGrain,
            "Matched to the photo's own measured grain."));

        RasterImage result = grainMatched;
        if (settings.EnableAiEnhance)
        {
            result = _faceEnhancer.Enhance(result, settings);
            project?.RecordStage(new PipelineStageResult(
                "AI Enhance (opt-in)", true, "User explicitly enabled generative face enhancement for this project."));
        }
        else
        {
            project?.RecordStage(new PipelineStageResult(
                "AI Enhance (opt-in)", false, "Not enabled for this project (off by default)."));
        }

        return result;
    }
}
