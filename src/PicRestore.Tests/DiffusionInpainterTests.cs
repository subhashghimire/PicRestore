using PicRestore.Core.Models;
using PicRestore.Restoration.Inpainting;
using Xunit;

namespace PicRestore.Tests;

public class DiffusionInpainterTests
{
    [Fact]
    public void Inpaint_NeverChangesAProtectedPixel_EvenIfMarkedDamaged()
    {
        var image = TestImages.WithWhitePatch(30, 30, 10, 10, 10);
        var mask = new DamageMask(30, 30);
        for (int y = 10; y < 20; y++)
        {
            for (int x = 10; x < 20; x++)
            {
                mask.MarkDamaged(x, y);
            }
        }

        mask.Protect(15, 15); // The user's "never touch" brush over one pixel inside the damaged patch.

        var settings = ProcessingSettings.CreateDefault();
        var result = new DiffusionInpainter().Inpaint(image, mask, settings);

        var original = image.GetPixel(15, 15);
        var repaired = result.GetPixel(15, 15);

        Assert.Equal(original.R, repaired.R, precision: 5);
        Assert.Equal(original.G, repaired.G, precision: 5);
        Assert.Equal(original.B, repaired.B, precision: 5);
    }

    [Fact]
    public void Inpaint_LeavesTheImageUnchanged_WhenReconstructionStrengthIsZero()
    {
        var image = TestImages.WithWhitePatch(20, 20, 5, 5, 6);
        var mask = new DamageMask(20, 20);
        for (int y = 5; y < 11; y++)
        {
            for (int x = 5; x < 11; x++)
            {
                mask.MarkDamaged(x, y);
            }
        }

        var settings = ProcessingSettings.CreateDefault();
        settings.ReconstructionStrength = 0f;

        var result = new DiffusionInpainter().Inpaint(image, mask, settings);

        var original = image.GetPixel(7, 7);
        var repaired = result.GetPixel(7, 7);
        Assert.Equal(original.R, repaired.R, precision: 5);
    }
}
