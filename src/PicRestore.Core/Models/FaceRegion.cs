namespace PicRestore.Core.Models;

/// <summary>A detected face, in full-resolution pixel coordinates, with the detector's confidence (0..1).</summary>
public sealed record FaceRegion(float X, float Y, float Width, float Height, float Score);
