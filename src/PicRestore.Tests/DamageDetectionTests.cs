using PicRestore.Core.Models;
using PicRestore.Restoration.DamageDetection;
using Xunit;

namespace PicRestore.Tests;

public class DamageDetectionTests
{
    [Fact]
    public void Detect_FlagsASyntheticWhitePatch()
    {
        var image = TestImages.WithWhitePatch(width: 40, height: 40, patchX: 15, patchY: 15, patchSize: 10);
        var detector = new CompositeDamageDetector();
        var settings = ProcessingSettings.CreateDefault();

        DamageMask mask = detector.Detect(image, settings);

        Assert.True(mask.IsRepairable(20, 20, settings.RepairThreshold));
        Assert.Equal(DamageType.WhiteOrBleachedPatch, mask.Type[mask.IndexOf(20, 20)]);
    }

    [Fact]
    public void Detect_DoesNotFlagARealisticGrainyPhoto()
    {
        // A little grain is realistic for any real photo; a perfectly flat field is not (see TestImages).
        var image = TestImages.CreateFlat(width: 30, height: 30, gray: 0.5f, noiseSigma: 0.1f, seed: 3);
        var detector = new CompositeDamageDetector();
        var settings = ProcessingSettings.CreateDefault();

        DamageMask mask = detector.Detect(image, settings);

        Assert.True(mask.CoveragePercentage(settings.RepairThreshold) < 5f);
    }

    [Fact]
    public void Detect_HigherSensitivityNeverFlagsFewerPixelsThanLowerSensitivity()
    {
        var image = TestImages.WithWhitePatch(width: 40, height: 40, patchX: 15, patchY: 15, patchSize: 6);
        var detector = new CompositeDamageDetector();

        ProcessingSettings lowSensitivity = ProcessingSettings.CreateDefault();
        lowSensitivity.DetectionSensitivity = 0.1f;
        ProcessingSettings highSensitivity = ProcessingSettings.CreateDefault();
        highSensitivity.DetectionSensitivity = 0.9f;

        DamageMask lowMask = detector.Detect(image, lowSensitivity);
        DamageMask highMask = detector.Detect(image, highSensitivity);

        Assert.True(highMask.CoveragePercentage(0.5f) >= lowMask.CoveragePercentage(0.5f));
    }
}
