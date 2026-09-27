namespace PicRestore.Restoration.Training;

/// <summary>
/// One before/after example prepared for detector training, at the detector's working resolution:
/// the damaged scan (planar RGB, 0..1), which pixels are damage (where the restored version differs
/// beyond what a global colour correction explains), and which pixels are "known" (covered by the
/// aligned restored image). Only derived data is kept - never the restored photo itself.
/// </summary>
public sealed class TrainingPair
{
    public required string Name { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required float[] R { get; init; }
    public required float[] G { get; init; }
    public required float[] B { get; init; }
    public required bool[] Damage { get; init; }
    public required bool[] Known { get; init; }

    /// <summary>Share of known pixels labelled as damage (0..1).</summary>
    public double DamageShare
    {
        get
        {
            long known = 0, damage = 0;
            for (int i = 0; i < Known.Length; i++)
            {
                if (Known[i])
                {
                    known++;
                    if (Damage[i])
                    {
                        damage++;
                    }
                }
            }

            return known == 0 ? 0 : (double)damage / known;
        }
    }

    private const int FormatVersion = 1;

    /// <summary>Compact binary: header, 8-bit RGB, 1 byte of flags per pixel (bit0 damage, bit1 known).</summary>
    public void Save(string path)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(0x50524950); // "PIRP"
        writer.Write(FormatVersion);
        writer.Write(Name);
        writer.Write(Width);
        writer.Write(Height);
        int n = Width * Height;
        var bytes = new byte[n * 4];
        for (int i = 0; i < n; i++)
        {
            bytes[i * 4] = ToByte(R[i]);
            bytes[(i * 4) + 1] = ToByte(G[i]);
            bytes[(i * 4) + 2] = ToByte(B[i]);
            bytes[(i * 4) + 3] = (byte)((Damage[i] ? 1 : 0) | (Known[i] ? 2 : 0));
        }

        writer.Write(bytes);
    }

    public static TrainingPair Load(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.ReadInt32() != 0x50524950 || reader.ReadInt32() != FormatVersion)
        {
            throw new InvalidDataException($"{path} is not a PicRestore training pair.");
        }

        string name = reader.ReadString();
        int width = reader.ReadInt32();
        int height = reader.ReadInt32();
        int n = width * height;
        byte[] bytes = reader.ReadBytes(n * 4);
        if (bytes.Length != n * 4)
        {
            throw new InvalidDataException($"{path} is truncated.");
        }

        var r = new float[n];
        var g = new float[n];
        var b = new float[n];
        var damage = new bool[n];
        var known = new bool[n];
        for (int i = 0; i < n; i++)
        {
            r[i] = bytes[i * 4] / 255f;
            g[i] = bytes[(i * 4) + 1] / 255f;
            b[i] = bytes[(i * 4) + 2] / 255f;
            damage[i] = (bytes[(i * 4) + 3] & 1) != 0;
            known[i] = (bytes[(i * 4) + 3] & 2) != 0;
        }

        return new TrainingPair { Name = name, Width = width, Height = height, R = r, G = g, B = b, Damage = damage, Known = known };
    }

    private static byte ToByte(float v) => (byte)Math.Clamp(MathF.Round(v * 255f), 0f, 255f);
}
