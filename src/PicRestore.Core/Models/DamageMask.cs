namespace PicRestore.Core.Models;

/// <summary>
/// A per-pixel record of where a photo is damaged, how confident that detection is, what kind of
/// damage it looks like, and which pixels the user has explicitly protected. Nothing downstream ever
/// touches a protected pixel, no matter how confident the detector is.
/// </summary>
public sealed class DamageMask
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>Damage probability per pixel, in [0, 1]. 0 means "confidently intact".</summary>
    public float[] Probability { get; }

    /// <summary>Best-guess damage classification per pixel, when known.</summary>
    public DamageType[] Type { get; }

    /// <summary>User-protected pixels ("never touch"), always excluded regardless of Probability.</summary>
    public bool[] Protected { get; }

    /// <summary>
    /// Faces whose intact pixels were locked against repair when this mask was built (see FaceLock).
    /// Informational - the lock itself is already reflected in <see cref="Probability"/>.
    /// </summary>
    public IReadOnlyList<FaceRegion> LockedFaces { get; set; } = Array.Empty<FaceRegion>();

    public DamageMask(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Mask dimensions must be positive.");

        Width = width;
        Height = height;

        int count = width * height;
        Probability = new float[count];
        Type = new DamageType[count];
        Protected = new bool[count];
    }

    public int IndexOf(int x, int y) => (y * Width) + x;

    public void SetDamage(int x, int y, float probability, DamageType type)
    {
        int i = IndexOf(x, y);
        Probability[i] = Math.Clamp(probability, 0f, 1f);
        Type[i] = type;
    }

    public void Protect(int x, int y) => Protected[IndexOf(x, y)] = true;

    public void Unprotect(int x, int y) => Protected[IndexOf(x, y)] = false;

    /// <summary>Manually mark a pixel as damaged (from the user's damage brush), independent of detection.</summary>
    public void MarkDamaged(int x, int y, DamageType type = DamageType.WhiteOrBleachedPatch)
    {
        int i = IndexOf(x, y);
        Probability[i] = 1f;
        Type[i] = type;
    }

    /// <summary>Manually mark a pixel as intact (the user erasing a false positive).</summary>
    public void MarkIntact(int x, int y)
    {
        int i = IndexOf(x, y);
        Probability[i] = 0f;
        Type[i] = DamageType.None;
    }

    // Damage types whose content is actually missing and calls for spatial reconstruction, rather than
    // just a colour/contrast correction in place. Kept as one place so the two members below (and any
    // future caller) can never drift out of sync with each other.
    private static bool IsReconstructableType(DamageType type) => type is
        DamageType.WhiteOrBleachedPatch or
        DamageType.CreaseScratchOrTear or
        DamageType.StainOrDiscoloration or
        DamageType.MetallicOrFoxingSpeckle;

    /// <summary>Whether a pixel is flagged for attention at all: above the threshold and not user-protected.
    /// This says nothing about HOW it should be handled - a faded pixel and a bleached-out one are both
    /// "repairable" by this definition, even though only one of them is missing content. Used for
    /// coverage display and for keeping already-flagged pixels out of the colour reference.</summary>
    public bool IsRepairable(int x, int y, float threshold)
    {
        int i = IndexOf(x, y);
        return !Protected[i] && Probability[i] >= threshold;
    }

    /// <summary>
    /// Whether a pixel should have its CONTENT reconstructed (spatially filled from context) rather than
    /// just colour/contrast-corrected in place. Per the design plan's damage-handling table, only actual
    /// missing content - a bleached-out patch, a crease/scratch/tear, a stain, or metallic/foxing
    /// speckle - calls for reconstruction; <see cref="DamageType.FadedLowContrast"/> and
    /// <see cref="DamageType.YellowingOrColorCast"/> pixels still hold their real detail; diffusion-filling
    /// them would destroy it to "fix" something the global colour restorer already handles losslessly.
    /// Manually brushed-in damage (via <see cref="MarkDamaged"/>) defaults to
    /// <see cref="DamageType.WhiteOrBleachedPatch"/>, so it is always reconstructable regardless of what
    /// the automatic detector would have called it.
    /// </summary>
    public bool IsReconstructable(int x, int y, float threshold)
    {
        int i = IndexOf(x, y);
        if (Protected[i] || Probability[i] < threshold)
        {
            return false;
        }

        return IsReconstructableType(Type[i]);
    }

    /// <summary>Share of the image (0-100) currently flagged for repair, ignoring protected pixels.</summary>
    public float CoveragePercentage(float threshold)
    {
        if (Probability.Length == 0)
            return 0f;

        int repaired = 0;
        for (int i = 0; i < Probability.Length; i++)
        {
            if (!Protected[i] && Probability[i] >= threshold)
                repaired++;
        }

        return 100f * repaired / Probability.Length;
    }

    /// <summary>
    /// Share of the image (0-100) that will actually be spatially reconstructed - a subset of
    /// <see cref="CoveragePercentage"/>, since not every flagged pixel calls for reconstruction (see
    /// <see cref="IsReconstructable"/>). This is what the "Reconstruction" pipeline stage
    /// should report, so the audit trail says what was actually rebuilt rather than everything merely
    /// flagged for some kind of attention.
    /// </summary>
    public float ReconstructedCoveragePercentage(float threshold)
    {
        if (Probability.Length == 0)
            return 0f;

        int reconstructed = 0;
        for (int i = 0; i < Probability.Length; i++)
        {
            bool reconstructable = !Protected[i] && Probability[i] >= threshold && IsReconstructableType(Type[i]);
            if (reconstructable)
                reconstructed++;
        }

        return 100f * reconstructed / Probability.Length;
    }

    public DamageMask Clone()
    {
        var clone = new DamageMask(Width, Height);
        Array.Copy(Probability, clone.Probability, Probability.Length);
        Array.Copy(Type, clone.Type, Type.Length);
        Array.Copy(Protected, clone.Protected, Protected.Length);
        clone.LockedFaces = LockedFaces;
        return clone;
    }
}
