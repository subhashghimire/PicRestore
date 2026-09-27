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

    /// <summary>
    /// 0 = leave damage untouched, 1 = fully automatic reconstruction. Defaults to 1: with the AI
    /// inpainter, anything less leaves a translucent ghost of the damage blended over the repair.
    /// </summary>
    public float ReconstructionStrength { get; set; } = 1f;

    /// <summary>
    /// How readily damage is flagged, in [0, 1]. Lower favours precision (fewer false positives on
    /// intact areas) over recall, per the "under-detection is the safer default" rule. The default of 0.2
    /// was chosen on five before/after pairs: compared with 0.4 it cut the change to intact pixels by
    /// roughly half to two thirds on the lightly damaged photos while scoring higher on average against the
    /// references. Raise it in the Mask Editor for heavily damaged prints.
    /// </summary>
    public float DetectionSensitivity { get; set; } = 0.2f;

    /// <summary>
    /// Final luminance sharpening (0 = off, 0.10 = the restoration protocol's "slight ~10%" pass). Only
    /// compensates for age blur; values much above 0.2 start to look like modern digital clarity.
    /// </summary>
    public float SharpeningAmount { get; set; } = 0.10f;

    /// <summary>
    /// Restoration protocol, "locked elements": inside detected faces only damage flagged with at least
    /// <see cref="FaceLockConfidence"/> (or painted with the damage brush) is repaired; everything else on a
    /// face is left exactly as it is.
    /// </summary>
    public bool LockFaces { get; set; } = true;

    /// <summary>Damage probability required to repair a pixel inside a locked face.</summary>
    public float FaceLockConfidence { get; set; } = 0.85f;

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

    /// <summary>
    /// When true, a failed identity check discards the whole reconstruction and keeps only the colour
    /// correction ("restore less rather than invent"). Off by default: the check is still a crude,
    /// size-based placeholder (no face detection yet), and on heavily damaged photos it rejected every
    /// useful repair. With it off, a failed check is recorded as a warning in the history instead.
    /// </summary>
    public bool StrictIdentityGuard { get; set; } = false;

    public static ProcessingSettings CreateDefault() => new();

    public ProcessingSettings Clone() => new()
    {
        ColorRestorationStrength = ColorRestorationStrength,
        ReconstructionStrength = ReconstructionStrength,
        DetectionSensitivity = DetectionSensitivity,
        MatchOriginalGrain = MatchOriginalGrain,
        EnableAiEnhance = EnableAiEnhance,
        RepairThreshold = RepairThreshold,
        StrictIdentityGuard = StrictIdentityGuard,
        SharpeningAmount = SharpeningAmount,
        LockFaces = LockFaces,
        FaceLockConfidence = FaceLockConfidence
    };
}
