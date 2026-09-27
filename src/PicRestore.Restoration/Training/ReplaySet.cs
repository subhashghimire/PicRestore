using System.Reflection;
using PicRestore.Restoration.DamageDetection;

namespace PicRestore.Restoration.Training;

/// <summary>
/// A compact "memory" of the examples the built-in detector was trained on: a few thousand feature
/// vectors (with labels) per built-in before/after pair, sampled at random pixels. Mixed into every
/// in-app training round so the model keeps what it already knew while it learns from the user's new
/// pairs (no catastrophic forgetting), and used as a check that a candidate model hasn't regressed on it.
/// It stores per-pixel statistics only - the built-in photos cannot be reconstructed from it.
/// </summary>
public sealed class ReplaySet
{
    public required float[] Features { get; init; }   // rows x DamageFeatures.Count
    public required bool[] Labels { get; init; }
    public required float[] Weights { get; init; }
    public required int PairCount { get; init; }

    public int Rows => Labels.Length;

    private const string ResourceName = "PicRestore.BuiltInReplay.bin";

    /// <summary>The replay set embedded in this assembly, or null if the build doesn't include one.</summary>
    public static ReplaySet? LoadBuiltIn()
    {
        using Stream? stream = typeof(ReplaySet).Assembly.GetManifestResourceStream(ResourceName);
        return stream is null ? null : Read(stream);
    }

    public static ReplaySet Read(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        if (reader.ReadInt32() != 0x53524950 || reader.ReadInt32() != 1) // "PIRS", v1
        {
            throw new InvalidDataException("Not a PicRestore replay set.");
        }

        int rows = reader.ReadInt32();
        int featureCount = reader.ReadInt32();
        int pairs = reader.ReadInt32();
        if (featureCount != DamageFeatures.Count)
        {
            throw new InvalidDataException("Replay set was built for a different feature layout.");
        }

        var features = new float[rows * featureCount];
        byte[] raw = reader.ReadBytes(features.Length * 2);
        for (int i = 0; i < features.Length; i++)
        {
            features[i] = (float)BitConverter.ToHalf(raw, i * 2);
        }

        byte[] labelBytes = reader.ReadBytes(rows);
        var weights = new float[rows];
        byte[] weightBytes = reader.ReadBytes(rows * 2);
        for (int i = 0; i < rows; i++)
        {
            weights[i] = (float)BitConverter.ToHalf(weightBytes, i * 2);
        }

        return new ReplaySet
        {
            Features = features,
            Labels = labelBytes.Select(v => v != 0).ToArray(),
            Weights = weights,
            PairCount = pairs,
        };
    }
}
