using PicRestore.Core.Models;
using PicRestore.Restoration.DamageDetection;
using Xunit;

namespace PicRestore.Tests;

public class LearnedDamageDetectorTests
{
    [Fact]
    public void Detect_ReturnsAFullSizeMaskWithValidProbabilities()
    {
        var image = TestImages.WithWhitePatch(64, 48, 20, 20, 12);
        var mask = new LearnedDamageDetector().Detect(image, ProcessingSettings.CreateDefault());

        Assert.Equal(64, mask.Width);
        Assert.Equal(48, mask.Height);
        Assert.True(mask.Probability.All(p => p >= 0f && p <= 1f && !float.IsNaN(p)));
    }

    [Fact]
    public void Detect_HigherSensitivityFlagsASupersetOfLowerSensitivity()
    {
        var image = TestImages.WithWhitePatch(64, 64, 20, 20, 10);
        var detector = new LearnedDamageDetector();
        var low = ProcessingSettings.CreateDefault();
        low.DetectionSensitivity = 0.1f;
        var high = ProcessingSettings.CreateDefault();
        high.DetectionSensitivity = 0.9f;

        DamageMask lowMask = detector.Detect(image, low);
        DamageMask highMask = detector.Detect(image, high);

        for (int i = 0; i < lowMask.Probability.Length; i++)
        {
            Assert.True(highMask.Probability[i] >= lowMask.Probability[i]);
        }
    }

    [Fact]
    public void Detect_GivesTheSameAnswerWhenServedFromItsCache()
    {
        var image = TestImages.CreateFlat(40, 40, 0.5f, noiseSigma: 0.1f, seed: 5);
        var detector = new LearnedDamageDetector();
        var settings = ProcessingSettings.CreateDefault();

        DamageMask first = detector.Detect(image, settings);
        DamageMask second = detector.Detect(image, settings); // same image instance: cached path

        Assert.True(first.Probability.SequenceEqual(second.Probability));
    }
}
