using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using PicRestore.Core.Imaging;

namespace PicRestore.App.ViewModels;

/// <summary>Converts between the engine's codec-agnostic <see cref="RasterImage"/> and a WinUI <see cref="WriteableBitmap"/> for on-screen display.</summary>
internal static class BitmapConverter
{
    public static async Task<WriteableBitmap> ToWriteableBitmapAsync(RasterImage image)
    {
        var bitmap = new WriteableBitmap(image.Width, image.Height);
        var bytes = new byte[image.Width * image.Height * 4];

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                (float r, float g, float b, float a) = image.GetPixel(x, y);
                int i = ((y * image.Width) + x) * 4;

                // WriteableBitmap's pixel buffer is BGRA8, premultiplied.
                bytes[i] = ToByte(b);
                bytes[i + 1] = ToByte(g);
                bytes[i + 2] = ToByte(r);
                bytes[i + 3] = ToByte(a);
            }
        }

        using System.IO.Stream stream = bitmap.PixelBuffer.AsStream();
        await stream.WriteAsync(bytes.AsMemory(0, bytes.Length));
        bitmap.Invalidate();
        return bitmap;
    }

    private static byte ToByte(float channel) => (byte)Math.Clamp(MathF.Round(channel * 255f), 0f, 255f);
}
