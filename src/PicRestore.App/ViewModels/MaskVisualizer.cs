using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using PicRestore.Core.Models;

namespace PicRestore.App.ViewModels;

/// <summary>
/// Renders a <see cref="DamageMask"/> as a translucent colour overlay for the Mask Editor and the
/// Compare page's confidence heat map: red where damage is flagged (opacity scaled by confidence),
/// blue where the user has painted a "never touch" protected region, transparent elsewhere.
/// </summary>
internal static class MaskVisualizer
{
    public static async Task<WriteableBitmap> RenderAsync(DamageMask mask, float threshold)
    {
        var bitmap = new WriteableBitmap(mask.Width, mask.Height);
        var bytes = new byte[mask.Width * mask.Height * 4];

        for (int y = 0; y < mask.Height; y++)
        {
            for (int x = 0; x < mask.Width; x++)
            {
                int i = ((y * mask.Width) + x) * 4;
                int maskIndex = mask.IndexOf(x, y);

                if (mask.Protected[maskIndex])
                {
                    // Blue, semi-opaque: "never touch". BGRA byte order.
                    bytes[i] = 220;
                    bytes[i + 1] = 40;
                    bytes[i + 2] = 20;
                    bytes[i + 3] = 200;
                }
                else if (mask.Probability[maskIndex] >= threshold)
                {
                    // Red, opacity scaled by confidence.
                    byte alpha = (byte)(mask.Probability[maskIndex] * 200);
                    bytes[i] = 20;
                    bytes[i + 1] = 20;
                    bytes[i + 2] = 220;
                    bytes[i + 3] = alpha;
                }
                else
                {
                    bytes[i] = 0;
                    bytes[i + 1] = 0;
                    bytes[i + 2] = 0;
                    bytes[i + 3] = 0;
                }
            }
        }

        using System.IO.Stream stream = bitmap.PixelBuffer.AsStream();
        await stream.WriteAsync(bytes.AsMemory(0, bytes.Length));
        bitmap.Invalidate();
        return bitmap;
    }
}
