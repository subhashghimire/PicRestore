namespace PicRestore.Core.Abstractions;

/// <summary>
/// A learned, fixed-size inpainting network (e.g. LaMa) that fills one square tile at a time. Kept as a
/// tiny interface in Core so the restoration pipeline has no dependency on any particular inference
/// runtime; the ONNX Runtime implementation lives in PicRestore.Ml.
/// </summary>
public interface IInpaintingModel
{
    /// <summary>Tile edge length the network expects (e.g. 512).</summary>
    int TileSize { get; }

    /// <summary>
    /// Fills one tile. <paramref name="imageBgrChw"/> is planar B,G,R (channel-major, TileSize x TileSize
    /// each) with values 0..1; <paramref name="mask"/> is TileSize x TileSize with 1 = fill, 0 = keep.
    /// Writes planar B,G,R values 0..255 into <paramref name="outputBgrChw"/>.
    /// </summary>
    void Run(float[] imageBgrChw, float[] mask, float[] outputBgrChw);
}
