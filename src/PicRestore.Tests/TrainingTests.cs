using PicRestore.Core.Imaging;
using PicRestore.Restoration.DamageDetection;
using PicRestore.Restoration.Training;
using Xunit;

namespace PicRestore.Tests;

public class TrainingTests
{
    /// <summary>A textured synthetic "photo": smooth gradients plus fine deterministic texture.</summary>
    private static RasterImage Scene(int w, int h)
    {
        var image = new RasterImage(w, h);
        var rng = new Random(11);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float t = (float)((rng.NextDouble() - 0.5) * 0.06);
                float r = 0.35f + (0.3f * x / w) + t, g = 0.3f + (0.25f * y / h) + t, b = 0.4f + (0.1f * MathF.Sin(x * 0.05f)) + t;
                image.SetPixel(x, y, r, g, b);
            }
        }

        return image;
    }

    private static RasterImage WithSpecks(RasterImage clean, int count, int seed)
    {
        RasterImage damaged = clean.Clone();
        var rng = new Random(seed);
        for (int k = 0; k < count; k++)
        {
            int cx = rng.Next(10, clean.Width - 10), cy = rng.Next(10, clean.Height - 10);
            for (int y = cy - 3; y <= cy + 3; y++)
            {
                for (int x = cx - 3; x <= cx + 3; x++)
                {
                    damaged.SetPixel(x, y, 0.97f, 0.97f, 0.95f);
                }
            }
        }

        return damaged;
    }

    private static RasterImage CropAndScale(RasterImage source, int x0, int y0, int cw, int ch, int outW, int outH)
    {
        var result = new RasterImage(outW, outH);
        for (int y = 0; y < outH; y++)
        {
            for (int x = 0; x < outW; x++)
            {
                int sx = x0 + (int)((x + 0.5) * cw / outW), sy = y0 + (int)((y + 0.5) * ch / outH);
                var p = source.GetPixel(sx, sy);
                result.SetPixel(x, y, p.R, p.G, p.B);
            }
        }

        return result;
    }

    /// <summary>Smooth, structured content (no per-pixel noise) so resampling doesn't alias it.</summary>
    private static RasterImage Shapes(int w, int h)
    {
        var image = new RasterImage(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float v = 0.5f + (0.25f * MathF.Sin(x * 0.07f) * MathF.Cos(y * 0.05f)) + (0.2f * MathF.Sin((x + (2 * y)) * 0.021f));
                image.SetPixel(x, y, v, 0.8f * v, 0.6f * v);
            }
        }

        return image;
    }

    [Fact]
    public void Aligner_RecoversACropAndRescale()
    {
        RasterImage damaged = Shapes(400, 300);
        // Restored = the region x 20..380, y 30..270 of the scan, resized to 300 x 200.
        RasterImage restored = CropAndScale(damaged, 20, 30, 360, 240, 300, 200);

        PairAlignment a = PairAligner.Align(damaged, restored);

        Assert.True(a.Score > 0.5, $"score {a.Score}");
        Assert.True(Math.Abs(a.ScaleX - 0.9) < 0.02, $"sx {a.ScaleX}");
        Assert.True(Math.Abs(a.ScaleY - 0.8) < 0.02, $"sy {a.ScaleY}");
        Assert.True(Math.Abs(a.OffsetX - 0.05) < 0.02, $"tx {a.OffsetX}");
        Assert.True(Math.Abs(a.OffsetY - 0.1) < 0.02, $"ty {a.OffsetY}");
    }

    [Fact]
    public void GroundTruth_MarksTheSpecksThatTheRestorationRemoved()
    {
        RasterImage clean = Scene(400, 300);
        RasterImage damaged = clean.Clone();
        var specks = new[] { (60, 50), (200, 150), (330, 240) };
        foreach ((int cx, int cy) in specks)
        {
            for (int y = cy - 3; y <= cy + 3; y++)
            {
                for (int x = cx - 3; x <= cx + 3; x++)
                {
                    damaged.SetPixel(x, y, 0.97f, 0.97f, 0.95f);
                }
            }
        }

        TrainingPair pair = GroundTruthBuilder.Build("synthetic", damaged, clean, new PairAlignment(1, 1, 0, 0, 1));

        foreach ((int cx, int cy) in specks)
        {
            Assert.True(pair.Damage[(cy * pair.Width) + cx], $"speck at {cx},{cy} not marked");
        }

        Assert.False(pair.Damage[(100 * pair.Width) + 120]);
        Assert.True(pair.DamageShare < 0.02, $"damage share {pair.DamageShare}");
    }

    [Fact]
    public void Pair_SurvivesASaveLoadRoundTrip()
    {
        RasterImage clean = Scene(200, 150);
        TrainingPair pair = GroundTruthBuilder.Build("roundtrip", WithSpecks(clean, 5, 9), clean, new PairAlignment(1, 1, 0, 0, 1));
        string path = Path.Combine(Path.GetTempPath(), $"picrestore-pair-{Guid.NewGuid():N}.bin");
        try
        {
            pair.Save(path);
            TrainingPair loaded = TrainingPair.Load(path);
            Assert.Equal(pair.Width, loaded.Width);
            Assert.True(pair.Damage.SequenceEqual(loaded.Damage));
            Assert.True(pair.Known.SequenceEqual(loaded.Known));
            Assert.Equal(pair.R[1234], loaded.R[1234], precision: 2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ModelWeights_SurviveAJsonRoundTrip_AndTheBuiltInModelValidates()
    {
        DamageModelWeights builtIn = DamageModelWeights.BuiltIn;
        builtIn.Validate();
        DamageModelWeights copy = DamageModelWeights.FromJson(builtIn.ToJson());
        Assert.True(builtIn.Weights[0].SequenceEqual(copy.Weights[0]));
        Assert.Equal(builtIn.Threshold, copy.Threshold);
    }

    [Fact]
    public void BuiltInReplaySet_IsEmbedded()
    {
        ReplaySet? replay = ReplaySet.LoadBuiltIn();
        Assert.NotNull(replay);
        Assert.True(replay!.Rows > 1000 && replay.Labels.Any(l => l) && replay.Labels.Any(l => !l));
    }

    [Fact]
    public void Trainer_LearnsSyntheticSpecks_AndOnlyAcceptsIfNotWorse()
    {
        RasterImage clean = Scene(320, 240);
        var pairs = new List<TrainingPair>();
        for (int k = 0; k < 2; k++)
        {
            pairs.Add(GroundTruthBuilder.Build($"p{k}", WithSpecks(clean, 60, 20 + k), clean, new PairAlignment(1, 1, 0, 0, 1)));
        }

        TrainingReport report = DamageModelTrainer.Train(pairs, DamageModelWeights.BuiltIn, replay: null,
            options: new MlpTrainer.Options { MaxEpochs = 8 });

        Assert.Equal(report.CandidateScore >= report.CurrentScore - 1e-4, report.Accepted);
        Assert.True(report.CandidateScore > 0.3, $"candidate held-out IoU {report.CandidateScore}");
        Assert.Same(report.Accepted ? report.Candidate : DamageModelWeights.BuiltIn, report.Accepted ? report.Model : report.Model);
    }

    [Fact]
    public void Library_StoresPairsModelsAndSettings()
    {
        string root = Path.Combine(Path.GetTempPath(), $"picrestore-lib-{Guid.NewGuid():N}");
        try
        {
            var library = new TrainingLibrary(root);
            RasterImage clean = Scene(200, 150);
            TrainingPair pair = GroundTruthBuilder.Build("lib", WithSpecks(clean, 5, 1), clean, new PairAlignment(1, 1, 0, 0, 1));

            TrainingPairInfo info = library.AddPair(pair, "damaged.jpg", "restored.jpg", 0.9);
            Assert.Single(library.ListPairs());
            Assert.Equal(pair.Width, library.LoadPair(info.Id).Width);

            Assert.Null(library.LoadActiveModel());
            library.SaveActiveModel(DamageModelWeights.BuiltIn.With(name: "custom"));
            Assert.Equal("custom", library.LoadActiveModel()!.Name);
            library.ResetModel();
            Assert.Null(library.LoadActiveModel());

            library.SaveSettings(new TrainingSettings { RetrainAutomatically = false });
            Assert.False(library.LoadSettings().RetrainAutomatically);

            library.RemovePair(info.Id);
            Assert.Empty(library.ListPairs());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
