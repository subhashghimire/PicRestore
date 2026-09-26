namespace PicRestore.Core.Imaging;

/// <summary>
/// A simple, dependency-free in-memory image: interleaved RGBA, row-major, channel values in [0, 1].
/// Kept as plain floats (rather than bytes) so colour and tonal restoration have headroom to work in
/// without banding, per the wide-gamut/linear-space requirement in the design plan.
/// </summary>
public sealed class RasterImage
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>Interleaved R,G,B,A per pixel, row-major, length == Width * Height * 4.</summary>
    public float[] Pixels { get; }

    public RasterImage(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be positive.");

        Width = width;
        Height = height;
        Pixels = new float[width * height * 4];
    }

    public RasterImage(int width, int height, float[] pixels)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be positive.");
        if (pixels.Length != width * height * 4)
            throw new ArgumentException("Pixel buffer size does not match width/height.", nameof(pixels));

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public RasterImage Clone()
    {
        var copy = new float[Pixels.Length];
        Array.Copy(Pixels, copy, Pixels.Length);
        return new RasterImage(Width, Height, copy);
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public (float R, float G, float B, float A) GetPixel(int x, int y)
    {
        int i = (y * Width + x) * 4;
        return (Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
    }

    public void SetPixel(int x, int y, float r, float g, float b, float a = 1f)
    {
        int i = (y * Width + x) * 4;
        Pixels[i] = r;
        Pixels[i + 1] = g;
        Pixels[i + 2] = b;
        Pixels[i + 3] = a;
    }

    /// <summary>Perceptual luminance (Rec. 709 weights) at a pixel, 0..1.</summary>
    public float GetLuminance(int x, int y)
    {
        var (r, g, b, _) = GetPixel(x, y);
        return (0.2126f * r) + (0.7152f * g) + (0.0722f * b);
    }
}
