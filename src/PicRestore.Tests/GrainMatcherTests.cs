using PicRestore.Core.Models;
using PicRestore.Restoration.Texture;
using Xunit;

namespace PicRestore.Tests;

public class GrainMatcherTests
{
    [Fact]
    public void Apply_DoesNothing_WhenMatchOriginalGrainIsDisabled()
    {
        var original = TestImages.CreateFlat(20, 20, 0.5f, noiseSigma: 0.1f);
        var reconstructed = TestImages.CreateFlat(20, 20, 0.5f, noiseSigma: 0f);
        var mask = new DamageMask(20, 20);
        mask.MarkDamaged(10, 10);

        var settings = ProcessingSettings.CreateDefault();
        settings.MatchOriginalGrain = false;

        var result = new GrainMatcher().Apply(original, reconstructed, mask, settings);

        Assert.Same(reconstructed, result);
    }

    [Fact]
    public void Apply_AddsNoiseOnlyInsideTheDamagedRegion()
    {
        var original = TestImages.CreateFlat(20, 20, 0.5f, noiseSigma: 0.15f, seed: 7);
        var reconstructed = TestImages.CreateFlat(20, 20, 0.5f, noiseSigma: 0f);
        var mask = new DamageMask(20, 20);
        mask.MarkDamaged(10, 10);

        var settings = ProcessingSettings.CreateDefault();
        settings.MatchOriginalGrain = true;

        var result = new GrainMatcher().Apply(original, reconstructed, mask, settings);

        // An untouched pixel stays exactly as the (noise-free) reconstruction produced it.
        var untouched = result.GetPixel(0, 0);
        Assert.Equal(0.5f, untouched.R, precision: 3);

        // The damaged pixel should have been perturbed away from the flat 0.5 reconstruction.
        var repaired = result.GetPixel(10, 10);
        Assert.NotEqual(0.5f, repaired.R);
    }
}
