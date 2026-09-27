using PicRestore.Core.Models;

namespace PicRestore.Restoration.Identity;

/// <summary>
/// Applies the restoration protocol's face lock to a damage mask: inside each detected face (expanded by
/// <see cref="Margin"/> to include hair line and jaw), pixels flagged with less than
/// <see cref="ProcessingSettings.FaceLockConfidence"/> are un-flagged, so only confidently detected or
/// hand-brushed damage on a face is ever repaired. Measured on a lightly damaged photo, the unlocked
/// detector had flagged parts of an intact face; with the lock they stay untouched.
/// </summary>
public static class FaceLock
{
    public const float Margin = 0.15f;

    /// <returns>How many pixels that would have been repaired were locked instead.</returns>
    public static int Apply(DamageMask mask, IReadOnlyList<FaceRegion> faces, ProcessingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(faces);
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.LockFaces || faces.Count == 0)
        {
            mask.LockedFaces = Array.Empty<FaceRegion>();
            return 0;
        }

        int unflagged = 0;
        foreach (FaceRegion face in faces)
        {
            int x0 = Math.Max(0, (int)MathF.Floor(face.X - (face.Width * Margin)));
            int y0 = Math.Max(0, (int)MathF.Floor(face.Y - (face.Height * Margin)));
            int x1 = Math.Min(mask.Width - 1, (int)MathF.Ceiling(face.X + (face.Width * (1 + Margin))));
            int y1 = Math.Min(mask.Height - 1, (int)MathF.Ceiling(face.Y + (face.Height * (1 + Margin))));
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int i = mask.IndexOf(x, y);
                    if (mask.Probability[i] > 0f && mask.Probability[i] < settings.FaceLockConfidence)
                    {
                        if (mask.Probability[i] >= settings.RepairThreshold)
                        {
                            unflagged++; // was going to be repaired; now locked
                        }

                        mask.Probability[i] = 0f;
                        mask.Type[i] = DamageType.None;
                    }
                }
            }
        }

        mask.LockedFaces = faces;
        return unflagged;
    }
}
