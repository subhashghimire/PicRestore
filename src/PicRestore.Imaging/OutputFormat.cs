namespace PicRestore.Imaging;

/// <summary>The export formats PicRestore offers, per the design plan's non-functional requirements.</summary>
public enum OutputFormat
{
    /// <summary>Lossless. Recommended for the archival "keep copy".</summary>
    Png,

    /// <summary>Lossy, adjustable quality.</summary>
    Jpeg
}
