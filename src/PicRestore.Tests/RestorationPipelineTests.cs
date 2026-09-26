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

        // Colour restoration, reconstruction, identity check, grain matching, AI-enhance (skipped but logged).
        Assert.Equal(5, project.History.Count);
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
