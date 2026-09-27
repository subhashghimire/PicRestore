using PicRestore.Core.Abstractions;
using PicRestore.Core.Imaging;
using PicRestore.Core.Models;

namespace PicRestore.Restoration;

/// <summary>
/// Runs the full restoration sequence for one photo, in the order the archival restoration protocol
/// prescribes: colour &amp; tonal correction referenced to the surviving intact areas, localized
/// reconstruction of genuinely damaged pixels only (with an identity check), grain matching on the
/// repaired pixels, the opt-in AI-enhance pass (off by default), and finally a slight (~10%) luminance
/// sharpening pass. Every stage's outcome is recorded on the project so the UI's history/confidence view
/// has something real to show, per the design plan's "nothing is a black box" requirement.
/// </summary>
public sealed class RestorationPipeline
{
    private readonly IColorRestorer _colorRestorer;
    private readonly IInpainter _inpainter;
    private readonly IGrainMatcher _grainMatcher;
    private readonly IFaceIdentityGuard _identityGuard;
    private readonly IFaceEnhancer _faceEnhancer;
    private readonly ISharpener _sharpener;

    public RestorationPipeline(
        IColorRestorer colorRestorer,
        IInpainter inpainter,
        IGrainMatcher grainMatcher,
        IFaceIdentityGuard identityGuard,
        IFaceEnhancer faceEnhancer)
        : this(colorRestorer, inpainter, grainMatcher, identityGuard, faceEnhancer, new Finishing.UnsharpMaskSharpener())
    {
    }

    public RestorationPipeline(
        IColorRestorer colorRestorer,
        IInpainter inpainter,
        IGrainMatcher grainMatcher,
        IFaceIdentityGuard identityGuard,
        IFaceEnhancer faceEnhancer,
        ISharpener sharpener)
    {
        _sharpener = sharpener ?? throw new ArgumentNullException(nameof(sharpener));
        _colorRestorer = colorRestorer ?? throw new ArgumentNullException(nameof(colorRestorer));
        _inpainter = inpainter ?? throw new ArgumentNullException(nameof(inpainter));
        _grainMatcher = grainMatcher ?? throw new ArgumentNullException(nameof(grainMatcher));
        _identityGuard = identityGuard ?? throw new ArgumentNullException(nameof(identityGuard));
        _faceEnhancer = faceEnhancer ?? throw new ArgumentNullException(nameof(faceEnhancer));
    }

    /// <summary>Fixed at 5 so the UI's progress bar and stage checklist can be laid out ahead of time.</summary>
    public const int StageCount = 5;

    public RasterImage Run(
        RasterImage original,
        DamageMask mask,
        ProcessingSettings settings,
        RestorationProject? project = null,
        IProgress<PipelineProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(settings);

        progress?.Report(new PipelineProgress("Restoring colour & tone", 1, StageCount));
        RasterImage colorCorrected = _colorRestorer.Restore(original, mask, settings);
        project?.RecordStage(new PipelineStageResult(
            "Colour & Tonal Restoration",
            settings.ColorRestorationStrength > 0f,
            $"Strength {settings.ColorRestorationStrength:P0}."));

        progress?.Report(new PipelineProgress("Reconstructing damaged areas", 2, StageCount));
        var diagnostics = _inpainter as IStageDiagnostics;
        if (diagnostics is not null)
        {
            diagnostics.StatusCallback = status => progress?.Report(new PipelineProgress(status, 2, StageCount));
        }

        RasterImage reconstructed;
        try
        {
            reconstructed = _inpainter.Inpaint(colorCorrected, mask, settings);
        }
        finally
        {
            if (diagnostics is not null)
            {
                diagnostics.StatusCallback = null;
            }
        }

        project?.RecordStage(new PipelineStageResult(
            "Reconstruction",
            settings.ReconstructionStrength > 0f,
            $"Strength {settings.ReconstructionStrength:P0}; repaired " +
            $"{mask.ReconstructedCoveragePercentage(settings.RepairThreshold):F1}% of pixels." +
            (mask.LockedFaces.Count > 0 ? $" {mask.LockedFaces.Count} face(s) locked - only confident damage on them was repaired." : "") +
            (diagnostics?.LastRunNote is { } note ? $" {note}" : "")));

        IdentityCheckResult identityCheck = _identityGuard.Validate(original, reconstructed, mask);
        if (identityCheck.Passed)
        {
            project?.RecordStage(new PipelineStageResult("Identity Check", true, identityCheck.Message));
        }
        else if (settings.StrictIdentityGuard)
        {
            // Strict mode: keep the colour-corrected (pre-reconstruction) image for this run rather than
            // ship a reconstruction the guard doesn't trust - "restore less" over "ship something we
            // can't vouch for".
            reconstructed = colorCorrected;
            project?.RecordStage(new PipelineStageResult(
                "Identity Check", false, $"{identityCheck.Message} Strict mode: kept the colour-corrected image without reconstruction."));
        }
        else
        {
            // Advisory mode (default): the guard is still a size-only placeholder with no face detection,
            // so keep the repair but make the warning visible in the history for the user to judge.
            project?.RecordStage(new PipelineStageResult(
                "Identity Check",
                false,
                $"Warning only (strict mode off): {identityCheck.Message} Review faces in the comparison view."));
        }

        progress?.Report(new PipelineProgress("Matching film grain", 3, StageCount));
        RasterImage grainMatched = _grainMatcher.Apply(original, reconstructed, mask, settings);
        project?.RecordStage(new PipelineStageResult(
            "Grain & Texture Matching",
            settings.MatchOriginalGrain,
            "Matched to the photo's own measured grain."));

        progress?.Report(new PipelineProgress(
            settings.EnableAiEnhance ? "Enhancing faces (AI, opt-in)" : "Skipping AI face enhancement (off)", 4, StageCount));
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

        progress?.Report(new PipelineProgress("Subtle sharpening", 5, StageCount));
        result = _sharpener.Sharpen(result, mask, settings);
        project?.RecordStage(new PipelineStageResult(
            "Subtle Sharpening",
            settings.SharpeningAmount > 0f,
            settings.SharpeningAmount > 0f
                ? $"Luminance unsharp mask at {settings.SharpeningAmount:P0} to offset age blur; protected pixels untouched."
                : "Off (amount 0)."));

        progress?.Report(new PipelineProgress("Done", StageCount, StageCount));
        return result;
    }
}
