namespace PicRestore.Core.Models;

/// <summary>
/// The distinct classes of Polaroid ageing damage the design plan calls out. Each is handled by a
/// different downstream stage rather than one generic "fix it" pass.
/// </summary>
public enum DamageType
{
    None,

    /// <summary>Missing content: reconstructed by the inpainting stage.</summary>
    WhiteOrBleachedPatch,

    /// <summary>No content lost, only colour: handled by colour restoration alone.</summary>
    YellowingOrColorCast,

    /// <summary>Colour correction first; only reconstructed if detail was also destroyed.</summary>
    StainOrDiscoloration,

    /// <summary>Missing content along a line: reconstructed, using the line as a continuation prior.</summary>
    CreaseScratchOrTear,

    /// <summary>No content lost, only dynamic range: contrast recovery only, never reconstruction.</summary>
    FadedLowContrast
}
