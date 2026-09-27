using PicRestore.Core.Abstractions;
using PicRestore.Core.Imaging;
using PicRestore.Core.Models;
using PicRestore.Restoration.Inpainting;
using Xunit;

namespace PicRestore.Tests;

public class LamaInpainterTests
{
    /// <summary>Stands in for the network: fills masked tile pixels with a fixed colour, keeps the rest.</summary>
    private sealed class SolidFillModel : IInpaintingModel
    {
        public const float Fill = 0.2f;
        public int Calls { get; private set; }
        public int TileSize => 512;

        public void Run(float[] image, float[] mask, float[] output)
        {
            Calls++;
            int plane = TileSize * TileSize;
            for (int c = 0; c < 3; c++)
            {
                for (int i = 0; i < plane; i++)
                {
                    output[(c * plane) + i] = mask[i] > 0.5f ? Fill * 255f : image[(c * plane) + i] * 255f;
                }
            }
        }
    }

    private static DamageMask SquareMask(int size, int from, int to)
    {
        var mask = new DamageMask(size, size);
        for (int y = from; y < to; y++)
        {
            for (int x = from; x < to; x++)
            {
                mask.MarkDamaged(x, y);
            }
        }

        return mask;
    }

    [Fact]
    public void Inpaint_FillsTheMaskAndLeavesDistantPixelsExactlyUntouched()
    {
        RasterImage image = TestImages.WithWhitePatch(600, 600, 250, 250, 100);
        DamageMask mask = SquareMask(600, 250, 350);
        var model = new SolidFillModel();

        RasterImage result = new LamaInpainter(model).Inpaint(image, mask, ProcessingSettings.CreateDefault());

        Assert.True(model.Calls > 0);
        Assert.Equal(SolidFillModel.Fill, result.GetPixel(300, 300).R, precision: 2);
        Assert.Equal(image.GetPixel(50, 50).R, result.GetPixel(50, 50).R, precision: 6);
        Assert.Equal(image.GetPixel(550, 400).G, result.GetPixel(550, 400).G, precision: 6);
    }

    [Fact]
    public void Inpaint_NeverChangesAProtectedPixel()
    {
        RasterImage image = TestImages.WithWhitePatch(600, 600, 250, 250, 100);
        DamageMask mask = SquareMask(600, 250, 350);
        mask.Protect(300, 300);
        mask.Protect(247, 300); // just outside the mask, inside the dilation/feather margin

        RasterImage result = new LamaInpainter(new SolidFillModel()).Inpaint(image, mask, ProcessingSettings.CreateDefault());

        Assert.Equal(image.GetPixel(300, 300).R, result.GetPixel(300, 300).R, precision: 6);
        Assert.Equal(image.GetPixel(247, 300).R, result.GetPixel(247, 300).R, precision: 6);
    }

    [Fact]
    public void Inpaint_FallsBackToDiffusionAndSaysSo_WhenNoModelIsAvailable()
    {
        RasterImage image = TestImages.WithWhitePatch(40, 40, 15, 15, 10);
        DamageMask mask = SquareMask(40, 15, 25);
        var inpainter = new LamaInpainter(model: null) { ModelUnavailableReason = "offline" };

        RasterImage result = inpainter.Inpaint(image, mask, ProcessingSettings.CreateDefault());

        Assert.True(inpainter.LastRunNote!.Contains("unavailable") && inpainter.LastRunNote.Contains("offline"));
        Assert.True(result.GetPixel(20, 20).R < 0.9f); // the white patch was filled from its grey surroundings
    }

    [Fact]
    public void Inpaint_ChangesNothing_WhenReconstructionStrengthIsZero()
    {
        RasterImage image = TestImages.WithWhitePatch(600, 600, 250, 250, 100);
        DamageMask mask = SquareMask(600, 250, 350);
        var settings = ProcessingSettings.CreateDefault();
        settings.ReconstructionStrength = 0f;

        RasterImage result = new LamaInpainter(new SolidFillModel()).Inpaint(image, mask, settings);

        Assert.True(image.Pixels.SequenceEqual(result.Pixels));
    }
}
