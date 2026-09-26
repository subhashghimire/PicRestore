namespace PicRestore.Core.Models;

/// <summary>
/// User-controllable knobs for one restoration run. Defaults are chosen to match the design plan's
/// "accurate, then conservative" ordering: automatic correction is on, but nothing generative is,
/// and detection favours precision over recall.
/// </summary>
public sealed class ProcessingSettings
{
    /// <summary>0 = leave colour untouched, 1 = full automatic colour/tone correction.</summary>
    public float ColorRestorationStrength { get; set; } = 0.75f;

    /// <summary>0 = leave damage untouched, 1 = fully automatic reconstruction.</summary>
    public float ReconstructionStrength { get; set; } = 0.75f;

    /// <summary>
    /// How readily damage is flagged, in [0, 1]. Lower favours precision (fewer false positives on
    /// intact areas) over recall, per the "under-detection is the safer default" rule.
    /// </summary>
    public float DetectionSensitivity { get; set; } = 0.4f;

    /// <summary>Re-synthesise film grain over reconstructed regions so they don't look artificially clean.</summary>
    public bool MatchOriginalGrain { get; set; } = true;

    /// <summary>
    /// Opt-in generative face enhancement (GFPGAN/CodeFormer/GPEN-style). OFF by default: these models
    /// can subtly alter facial identity, which conflicts with the default conservative-restoration
    /// guarantee. Must be turned on explicitly, per project, by the user.
    /// </summary>
    public bool EnableAiEnhance { get; set; } = false;

    /// <summary>Minimum damage probability treated as "repair this pixel" by the mask.</summary>
    public float RepairThreshold { get; set; } = 0.5f;

    public static ProcessingSettings CreateDefault() => new();

    public ProcessingSettings Clone() => new()
    {
        ColorRestorationStrength = ColorRestorationStrength,
        ReconstructionStrength = ReconstructionStrength,
        DetectionSensitivity = DetectionSensitivity,
        MatchOriginalGrain = MatchOriginalGrain,
        EnableAiEnhance = EnableAiEnhance,
        RepairThreshold = RepairThreshold
    };
}
