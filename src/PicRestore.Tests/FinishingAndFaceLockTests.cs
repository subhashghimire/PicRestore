using PicRestore.Core.Imaging;
using PicRestore.Core.Models;
using PicRestore.Restoration.Finishing;
using PicRestore.Restoration.Identity;
using Xunit;

namespace PicRestore.Tests;

public class FinishingAndFaceLockTests
{
    private static RasterImage Edge(int size)
    {
        var image = new RasterImage(size, size);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float v = x < size / 2 ? 0.3f : 0.7f;
                image.SetPixel(x, y, v, v * 0.9f, v * 0.8f);
            }
        }

        return image;
    }

    [Fact]
    public void Sharpen_IsANoOp_AtZeroAmount()
    {
        RasterImage image = Edge(40);
        var settings = ProcessingSettings.CreateDefault();
        settings.SharpeningAmount = 0f;

        RasterImage result = new UnsharpMaskSharpener().Sharpen(image, new DamageMask(40, 40), settings);

        Assert.True(image.Pixels.SequenceEqual(result.Pixels));
    }

    [Fact]
    public void Sharpen_IncreasesEdgeContrastSlightly_AndShiftsChannelsEqually()
    {
        RasterImage image = Edge(40);
        RasterImage result = new UnsharpMaskSharpener().Sharpen(image, new DamageMask(40, 40), ProcessingSettings.CreateDefault());

        var before = image.GetPixel(20, 20);
        var after = result.GetPixel(20, 20); // first pixel on the bright side of the edge gets a little brighter
        Assert.True(after.R > before.R && after.R - before.R <= 0.04f + 1e-6f);
        Assert.Equal(after.R - before.R, after.G - before.G, precision: 5);
        Assert.Equal(after.R - before.R, after.B - before.B, precision: 5);
    }

    [Fact]
    public void Sharpen_NeverTouchesAProtectedPixel()
    {
        RasterImage image = Edge(40);
        var mask = new DamageMask(40, 40);
        mask.Protect(21, 20);

        RasterImage result = new UnsharpMaskSharpener().Sharpen(image, mask, ProcessingSettings.CreateDefault());

        Assert.Equal(image.GetPixel(21, 20).R, result.GetPixel(21, 20).R, precision: 6);
    }

    [Fact]
    public void FaceLock_UnflagsUncertainDamageOnAFace_ButKeepsConfidentAndBrushedDamage()
    {
        var mask = new DamageMask(100, 100);
        mask.SetDamage(50, 50, 0.6f, DamageType.MetallicOrFoxingSpeckle);  // uncertain, on the face
        mask.SetDamage(55, 55, 0.95f, DamageType.MetallicOrFoxingSpeckle); // confident, on the face
        mask.MarkDamaged(45, 45);                                         // user brushed
        mask.SetDamage(5, 5, 0.6f, DamageType.MetallicOrFoxingSpeckle);    // uncertain, off the face
        var faces = new[] { new FaceRegion(40, 40, 25, 25, 0.9f) };

        int locked = FaceLock.Apply(mask, faces, ProcessingSettings.CreateDefault());

        Assert.Equal(1, locked);
        Assert.Equal(0f, mask.Probability[mask.IndexOf(50, 50)]);
        Assert.Equal(0.95f, mask.Probability[mask.IndexOf(55, 55)], precision: 5);
        Assert.Equal(1f, mask.Probability[mask.IndexOf(45, 45)], precision: 5);
        Assert.Equal(0.6f, mask.Probability[mask.IndexOf(5, 5)], precision: 5);
        Assert.Equal(1, mask.LockedFaces.Count);
    }

    [Fact]
    public void FaceLock_DoesNothing_WhenTurnedOff()
    {
        var mask = new DamageMask(100, 100);
        mask.SetDamage(50, 50, 0.6f, DamageType.MetallicOrFoxingSpeckle);
        var settings = ProcessingSettings.CreateDefault();
        settings.LockFaces = false;

        FaceLock.Apply(mask, new[] { new FaceRegion(40, 40, 25, 25, 0.9f) }, settings);

        Assert.Equal(0.6f, mask.Probability[mask.IndexOf(50, 50)], precision: 5);
        Assert.Equal(0, mask.LockedFaces.Count);
    }

    [Fact]
    public void YuNetDecode_TurnsOneConfidentCellIntoABox()
    {
        var outputs = new Dictionary<string, float[]>();
        foreach (int stride in new[] { 8, 16, 32 })
        {
            int cells = (640 / stride) * (640 / stride);
            outputs[$"cls_{stride}"] = new float[cells];
            outputs[$"obj_{stride}"] = new float[cells];
            outputs[$"bbox_{stride}"] = new float[cells * 4];
        }

        // Stride 32, row 3, column 5: centre at ((5 + 0.5) * 32, (3 + 0.5) * 32), size exp(1) * 32.
        int idx = (3 * 20) + 5;
        outputs["cls_32"][idx] = 0.9f;
        outputs["obj_32"][idx] = 0.9f;
        outputs["bbox_32"][(idx * 4) + 0] = 0.5f;
        outputs["bbox_32"][(idx * 4) + 1] = 0.5f;
        outputs["bbox_32"][(idx * 4) + 2] = 1f;
        outputs["bbox_32"][(idx * 4) + 3] = 1f;

        List<FaceRegion> faces = YuNetFaceDetector.Decode(outputs);

        Assert.Equal(1, faces.Count);
        float size = MathF.Exp(1f) * 32f;
        Assert.Equal((5.5f * 32f) - (size / 2f), faces[0].X, precision: 3);
        Assert.Equal((3.5f * 32f) - (size / 2f), faces[0].Y, precision: 3);
        Assert.Equal(0.9f, faces[0].Score, precision: 4);
    }
}
