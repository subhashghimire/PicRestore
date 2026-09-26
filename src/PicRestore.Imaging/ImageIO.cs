using PicRestore.Core.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace PicRestore.Imaging;

/// <summary>
/// Loads JPEG/JPG/PNG/BMP/TIFF into a codec-agnostic <see cref="RasterImage"/> and saves a
/// <see cref="RasterImage"/> back out as PNG or JPEG. ImageSharp auto-detects the input format from
/// its contents, not just the file extension, so a mislabelled file still loads correctly.
/// </summary>
public static class ImageIO
{
    public static RasterImage Load(string path)
    {
        using Image<Rgba32> source = Image.Load<Rgba32>(path);

        var image = new RasterImage(source.Width, source.Height);

        source.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    Rgba32 p = row[x];
                    image.SetPixel(
                        x, y,
                        p.R / 255f,
                        p.G / 255f,
                        p.B / 255f,
                        p.A / 255f);
                }
            }
        });

        return image;
    }

    public static void Save(RasterImage image, string path, OutputFormat format, int jpegQuality = 95)
    {
        using var target = new Image<Rgba32>(image.Width, image.Height);

        target.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    var (r, g, b, a) = image.GetPixel(x, y);
                    row[x] = new Rgba32(ToByte(r), ToByte(g), ToByte(b), ToByte(a));
                }
            }
        });

        switch (format)
        {
            case OutputFormat.Png:
                target.Save(path, new PngEncoder());
                break;
            case OutputFormat.Jpeg:
                target.Save(path, new JpegEncoder { Quality = Math.Clamp(jpegQuality, 1, 100) });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported output format.");
        }
    }

    private static byte ToByte(float channel) =>
        (byte)Math.Clamp(MathF.Round(channel * 255f), 0f, 255f);
}
