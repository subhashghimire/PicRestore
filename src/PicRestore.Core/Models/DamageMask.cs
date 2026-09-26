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

    /// <summary>Whether a pixel should be repaired: above the threshold and not user-protected.</summary>
    public bool IsRepairable(int x, int y, float threshold)
    {
        int i = IndexOf(x, y);
        return !Protected[i] && Probability[i] >= threshold;
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

    public DamageMask Clone()
    {
        var clone = new DamageMask(Width, Height);
        Array.Copy(Probability, clone.Probability, Probability.Length);
        Array.Copy(Type, clone.Type, Type.Length);
        Array.Copy(Protected, clone.Protected, Protected.Length);
        return clone;
    }
}
