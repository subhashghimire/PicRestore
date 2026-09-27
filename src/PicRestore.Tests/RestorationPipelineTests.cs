using PicRestore.Core.Abstractions;
using PicRestore.Core.Imaging;
using PicRestore.Core.Models;
using PicRestore.Restoration;
using PicRestore.Restoration.Color;
using PicRestore.Restoration.Enhancement;
using PicRestore.Restoration.Identity;
using PicRestore.Restoration.Inpainting;
using PicRestore.Restoration.Texture;
using Xunit;

namespace PicRestore.Tests;

public class RestorationPipelineTests
{
    [Fact]
    public void DefaultSettings_AiEnhanceIsOff()
    {
        var settings = ProcessingSettings.CreateDefault();

        Assert.False(settings.EnableAiEnhance);
    }

    [Fact]
    public void Run_DoesNotCallTheEnhancer_WhenAiEnhanceIsDisabled()
    {
        var enhancer = new RecordingFaceEnhancer();
        RestorationPipeline pipeline = BuildPipeline(enhancer);
        RasterImage image = TestImages.CreateFlat(20, 20, 0.5f, noiseSigma: 0.05f);
        var mask = new DamageMask(20, 20);
        ProcessingSettings settings = ProcessingSettings.CreateDefault();
        settings.EnableAiEnhance = false;

        RasterImage result = pipeline.Run(image, mask, settings);

        Assert.NotNull(result);
        Assert.False(enhancer.WasCalled);
    }

    [Fact]
    public void Run_CallsTheEnhancer_WhenAiEnhanceIsExplicitlyEnabled()
    {
        var enhancer = new RecordingFaceEnhancer();
        RestorationPipeline pipeline = BuildPipeline(enhancer);
        RasterImage image = TestImages.CreateFlat(20, 20, 0.5f, noiseSigma: 0.05f);
        var mask = new DamageMask(20, 20);
        ProcessingSettings settings = ProcessingSettings.CreateDefault();
        settings.EnableAiEnhance = true;

        pipeline.Run(image, mask, settings);

        Assert.True(enhancer.WasCalled);
    }

    [Fact]
    public void Run_RecordsOneHistoryEntryPerStage_WhenGivenAProject()
    {
        RestorationPipeline pipeline = BuildPipeline(new NullFaceEnhancer());
        RasterImage image = TestImages.CreateFlat(20, 20, 0.5f, noiseSigma: 0.05f);
        var mask = new DamageMask(20, 20);
        var project = new RestorationProject { OriginalFilePath = "test.png" };

        pipeline.Run(image, mask, ProcessingSettings.CreateDefault(), project);

        // Colour restoration, reconstruction, identity check, grain matching, AI-enhance (skipped but logged),
        // subtle sharpening.
        Assert.Equal(6, project.History.Count);
    }

    [Fact]
    public void Run_KeepsTheReconstruction_WhenTheGuardFailsInAdvisoryMode()
    {
        var pipeline = new RestorationPipeline(new HistogramColorRestorer(), new MarkerInpainter(), new GrainMatcher(), new AlwaysFailGuard(), new NullFaceEnhancer());
        var settings = ProcessingSettings.CreateDefault();
        settings.MatchOriginalGrain = false;
        settings.SharpeningAmount = 0f;
        var project = new RestorationProject { OriginalFilePath = "test.png" };

        RasterImage result = pipeline.Run(TestImages.CreateFlat(10, 10, 0.5f), new DamageMask(10, 10), settings, project);

        Assert.Equal(MarkerInpainter.Marker, result.GetPixel(0, 0).R, precision: 5);
        Assert.Contains(project.History, h => h.StageName == "Identity Check" && !h.Applied && h.Message.StartsWith("Warning only"));
    }

    [Fact]
    public void Run_DiscardsTheReconstruction_WhenTheGuardFailsInStrictMode()
    {
        var pipeline = new RestorationPipeline(new HistogramColorRestorer(), new MarkerInpainter(), new GrainMatcher(), new AlwaysFailGuard(), new NullFaceEnhancer());
        var settings = ProcessingSettings.CreateDefault();
        settings.MatchOriginalGrain = false;
        settings.SharpeningAmount = 0f;
        settings.StrictIdentityGuard = true;

        RasterImage result = pipeline.Run(TestImages.CreateFlat(10, 10, 0.5f), new DamageMask(10, 10), settings);

        Assert.NotEqual(MarkerInpainter.Marker, result.GetPixel(0, 0).R);
    }

    private sealed class MarkerInpainter : IInpainter
    {
        public const float Marker = 0.123f;

        public RasterImage Inpaint(RasterImage image, DamageMask mask, ProcessingSettings settings)
        {
            var output = image.Clone();
            output.SetPixel(0, 0, Marker, Marker, Marker);
            return output;
        }
    }

    private sealed class AlwaysFailGuard : IFaceIdentityGuard
    {
        public IdentityCheckResult Validate(RasterImage original, RasterImage candidate, DamageMask mask) =>
            new(false, 1f, "Test guard always fails.");
    }

    private static RestorationPipeline BuildPipeline(IFaceEnhancer enhancer) => new(
        new HistogramColorRestorer(),
        new DiffusionInpainter(),
        new GrainMatcher(),
        new SimpleFaceIdentityGuard(),
        enhancer);

    private sealed class RecordingFaceEnhancer : IFaceEnhancer
    {
        public bool WasCalled { get; private set; }

        public RasterImage Enhance(RasterImage image, ProcessingSettings settings)
        {
            WasCalled = true;
            return image;
        }
    }
}
