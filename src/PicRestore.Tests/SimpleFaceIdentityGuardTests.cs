using PicRestore.Core.Models;
using PicRestore.Restoration.Identity;
using Xunit;

namespace PicRestore.Tests;

public class SimpleFaceIdentityGuardTests
{
    [Fact]
    public void Validate_PassesForASmallRepairedRegion()
    {
        var mask = new DamageMask(50, 50);
        for (int y = 20; y < 25; y++)
        {
            for (int x = 20; x < 25; x++)
            {
                mask.MarkDamaged(x, y);
            }
        }

        var guard = new SimpleFaceIdentityGuard();
        var original = TestImages.CreateFlat(50, 50, 0.5f);

        var result = guard.Validate(original, original, mask);

        Assert.True(result.Passed);
    }

    [Fact]
    public void Validate_FailsForALargeContiguousRepairedRegion()
    {
        var mask = new DamageMask(50, 50);
        for (int y = 0; y < 40; y++)
        {
            for (int x = 0; x < 40; x++)
            {
                mask.MarkDamaged(x, y);
            }
        }

        var guard = new SimpleFaceIdentityGuard();
        var original = TestImages.CreateFlat(50, 50, 0.5f);

        var result = guard.Validate(original, original, mask);

        Assert.False(result.Passed);
    }
}
