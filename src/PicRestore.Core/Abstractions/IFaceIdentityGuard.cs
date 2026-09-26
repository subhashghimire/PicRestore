using PicRestore.Core.Imaging;
using PicRestore.Core.Models;

namespace PicRestore.Core.Abstractions;

/// <summary>Result of comparing a reconstructed image against the original for identity drift.</summary>
public sealed record IdentityCheckResult(bool Passed, float DeviationScore, string Message)
{
    public static IdentityCheckResult Ok(string message = "No face regions to check.") => new(true, 0f, message);
}

/// <summary>
/// Checks that a reconstruction hasn't shifted facial geometry beyond a small tolerance. A failing
/// result should cause the pipeline to fall back to a more conservative reconstruction rather than
/// keep the flagged output.
///
/// The Phase-1 implementation is a conservative heuristic placeholder (see
/// PicRestore.Restoration.Identity.SimpleFaceIdentityGuard); a real facial-landmark model is a
/// Phase-2 item, tracked in the design plan's roadmap.
/// </summary>
public interface IFaceIdentityGuard
{
    IdentityCheckResult Validate(RasterImage original, RasterImage candidate, DamageMask mask);
}
