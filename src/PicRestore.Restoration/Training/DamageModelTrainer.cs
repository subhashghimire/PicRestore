using PicRestore.Restoration.DamageDetection;
using PicRestore.Restoration.Imaging;

namespace PicRestore.Restoration.Training;

/// <summary>Progress of an in-app training run: a short stage description and overall completion 0..1.</summary>
public readonly record struct TrainingProgress(string Stage, double Fraction);

/// <summary>How the current and the candidate model scored on one pair's held-out regions.</summary>
public sealed record PairScore(string Name, double CurrentIoU, double CandidateIoU, double CurrentFalseFlagShare, double CandidateFalseFlagShare);

/// <summary>Outcome of <see cref="DamageModelTrainer.Train"/>.</summary>
public sealed record TrainingReport
{
    /// <summary>True if the candidate is at least as good on held-out data and kept the built-in knowledge.</summary>
    public required bool Accepted { get; init; }

    /// <summary>The model to use from now on: the candidate if accepted, otherwise the current one.</summary>
    public required DamageModelWeights Model { get; init; }

    public required DamageModelWeights Candidate { get; init; }
    public required double CurrentScore { get; init; }
    public required double CandidateScore { get; init; }
    public double? CurrentReplayScore { get; init; }
    public double? CandidateReplayScore { get; init; }
    public required IReadOnlyList<PairScore> Pairs { get; init; }
    public required TimeSpan Duration { get; init; }
    public required string Summary { get; init; }
}

/// <summary>
/// Continuous, safe improvement of the damage detector from before/after pairs:
/// <list type="number">
/// <item>Every pair is split into a 4x4 grid of blocks; one block per row (25%) is held out for validation.</item>
/// <item>A candidate is trained on the remaining blocks of all pairs plus the built-in replay set, starting
/// from the current model's weights.</item>
/// <item>The candidate is accepted only if its mean IoU on the held-out blocks is at least the current
/// model's, and it keeps at least 95% of the current model's score on the built-in replay set. Otherwise
/// the current model stays in place and the report says why - training can never make detection worse
/// on the data it has seen.</item>
/// </list>
/// </summary>
public static class DamageModelTrainer
{
    /// <summary>Training pixels drawn per pair (positives boosted to at least 30%).</summary>
    public const int SamplesPerPair = 60_000;

    public const double PositiveShareFloor = 0.30;

    public static TrainingReport Train(
        IReadOnlyList<TrainingPair> pairs,
        DamageModelWeights current,
        ReplaySet? replay = null,
        IProgress<TrainingProgress>? progress = null,
        MlpTrainer.Options? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(current);
        if (pairs.Count == 0)
        {
            throw new ArgumentException("Add at least one before/after pair first.", nameof(pairs));
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        const int f = DamageFeatures.Count;
        var rng = new Random(1234);
        var trainFeatures = new List<float[]>();
        var trainLabels = new List<bool[]>();
        var trainWeights = new List<float[]>();
        var currentScores = new List<(double IoU, double FalseShare)>();

        // Pass 1: features, training samples, and the current model's held-out score for each pair.
        for (int p = 0; p < pairs.Count; p++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TrainingPair pair = pairs[p];
            progress?.Report(new TrainingProgress($"Preparing \"{pair.Name}\" ({p + 1} of {pairs.Count})", 0.3 * p / pairs.Count));
            float[] features = DamageFeatures.Compute(pair.R, pair.G, pair.B, pair.Width, pair.Height);
            bool[] heldOut = HeldOutBlocks(pair.Width, pair.Height);

            (float[] x, bool[] y, float[] w) = SampleTraining(pair, features, heldOut, rng);
            trainFeatures.Add(x);
            trainLabels.Add(y);
            trainWeights.Add(w);

            float[] prob = LearnedDamageDetector.PredictWorking(current, features, pair.Width, pair.Height);
            currentScores.Add(Score(prob, current.Threshold, pair, heldOut));
        }

        // Replay: 80% trains, 20% (every 5th row) checks for forgetting.
        if (replay is not null)
        {
            var rx = new List<float>();
            var ry = new List<bool>();
            var rw = new List<float>();
            for (int i = 0; i < replay.Rows; i++)
            {
                if (i % 5 == 0)
                {
                    continue;
                }

                rx.AddRange(new ArraySegment<float>(replay.Features, i * f, f));
                ry.Add(replay.Labels[i]);
                rw.Add(replay.Weights[i]);
            }

            trainFeatures.Add(rx.ToArray());
            trainLabels.Add(ry.ToArray());
            trainWeights.Add(rw.ToArray());
        }

        float[] allX = trainFeatures.SelectMany(a => a).ToArray();
        bool[] allY = trainLabels.SelectMany(a => a).ToArray();
        float[] allW = trainWeights.SelectMany(a => a).ToArray();

        // Train.
        DamageModelWeights candidate = MlpTrainer.Train(
            current, allX, allY, allW, options,
            (epoch, max, loss) => progress?.Report(new TrainingProgress($"Training (round {epoch} of up to {max})", 0.3 + (0.5 * epoch / max))),
            cancellationToken);

        // Pass 2: candidate's held-out score per pair.
        var pairScores = new List<PairScore>();
        for (int p = 0; p < pairs.Count; p++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TrainingPair pair = pairs[p];
            progress?.Report(new TrainingProgress($"Checking the new model on \"{pair.Name}\"", 0.8 + (0.2 * p / pairs.Count)));
            float[] features = DamageFeatures.Compute(pair.R, pair.G, pair.B, pair.Width, pair.Height);
            float[] prob = LearnedDamageDetector.PredictWorking(candidate, features, pair.Width, pair.Height);
            (double iou, double falseShare) = Score(prob, candidate.Threshold, pair, HeldOutBlocks(pair.Width, pair.Height));
            pairScores.Add(new PairScore(pair.Name, currentScores[p].IoU, iou, currentScores[p].FalseShare, falseShare));
        }

        double currentScore = pairScores.Average(s => s.CurrentIoU);
        double candidateScore = pairScores.Average(s => s.CandidateIoU);
        double? currentReplay = null, candidateReplay = null;
        if (replay is not null)
        {
            (currentReplay, candidateReplay) = ReplayScores(replay, current, candidate);
        }

        bool better = candidateScore >= currentScore - 1e-4;
        bool remembers = replay is null || candidateReplay >= 0.95 * currentReplay;
        bool accepted = better && remembers;

        DamageModelWeights acceptedModel = candidate.With(
            name: $"Trained {DateTime.Now:yyyy-MM-dd HH:mm}",
            pairCount: current.TrainingPairCount + pairs.Count,
            validationIoU: candidateScore);

        string summary = accepted
            ? $"New model accepted: held-out damage-map overlap (IoU) {currentScore:P1} -> {candidateScore:P1} across {pairs.Count} pair(s)."
            : !better
                ? $"New model rejected: it scored {candidateScore:P1} on held-out regions versus {currentScore:P1} for the current model. The current model stays in use."
                : $"New model rejected: it lost too much of what the built-in model knew ({candidateReplay:P1} vs {currentReplay:P1} on the built-in examples). The current model stays in use.";

        return new TrainingReport
        {
            Accepted = accepted,
            Model = accepted ? acceptedModel : current,
            Candidate = acceptedModel,
            CurrentScore = currentScore,
            CandidateScore = candidateScore,
            CurrentReplayScore = currentReplay,
            CandidateReplayScore = candidateReplay,
            Pairs = pairScores,
            Duration = clock.Elapsed,
            Summary = summary,
        };
    }

    /// <summary>4x4 blocks; in block-row r the held-out column is (r * 3 + 1) mod 4 - 25% of each pair, spread out.</summary>
    public static bool[] HeldOutBlocks(int width, int height)
    {
        var held = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            int by = y * 4 / height;
            int column = ((by * 3) + 1) % 4;
            for (int x = 0; x < width; x++)
            {
                held[(y * width) + x] = x * 4 / width == column;
            }
        }

        return held;
    }

    private static (float[] X, bool[] Y, float[] W) SampleTraining(TrainingPair pair, float[] features, bool[] heldOut, Random rng)
    {
        const int f = DamageFeatures.Count;
        var positives = new List<int>();
        var negatives = new List<int>();
        for (int i = 0; i < pair.Known.Length; i++)
        {
            if (!pair.Known[i] || heldOut[i])
            {
                continue;
            }

            (pair.Damage[i] ? positives : negatives).Add(i);
        }

        int total = Math.Min(SamplesPerPair, positives.Count + negatives.Count);
        double naturalShare = positives.Count / (double)Math.Max(1, positives.Count + negatives.Count);
        int wantPositives = Math.Min(positives.Count, (int)(total * Math.Max(PositiveShareFloor, naturalShare)));
        int wantNegatives = Math.Min(negatives.Count, total - wantPositives);

        var chosen = Pick(positives, wantPositives, rng).Concat(Pick(negatives, wantNegatives, rng)).ToArray();
        var x = new float[chosen.Length * f];
        var y = new bool[chosen.Length];
        var w = new float[chosen.Length];
        for (int k = 0; k < chosen.Length; k++)
        {
            Array.Copy(features, chosen[k] * f, x, k * f, f);
            y[k] = pair.Damage[chosen[k]];
            w[k] = 1f;
        }

        return (x, y, w);
    }

    private static IEnumerable<int> Pick(List<int> source, int count, Random rng)
    {
        if (count >= source.Count)
        {
            return source;
        }

        var copy = source.ToArray();
        for (int i = 0; i < count; i++)
        {
            int j = i + rng.Next(copy.Length - i);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }

        return copy.Take(count);
    }

    private static (double IoU, double FalseShare) Score(float[] prob, float threshold, TrainingPair pair, bool[] heldOut)
    {
        long tp = 0, flagged = 0, truth = 0, union = 0;
        for (int i = 0; i < prob.Length; i++)
        {
            if (!heldOut[i] || !pair.Known[i])
            {
                continue;
            }

            bool p = prob[i] > threshold, g = pair.Damage[i];
            if (p && g) tp++;
            if (p) flagged++;
            if (g) truth++;
            if (p || g) union++;
        }

        return (union == 0 ? 1.0 : (double)tp / union, flagged == 0 ? 0.0 : (double)(flagged - tp) / flagged);
    }

    private static (double Current, double Candidate) ReplayScores(ReplaySet replay, DamageModelWeights current, DamageModelWeights candidate)
    {
        const int f = DamageFeatures.Count;
        var rows = Enumerable.Range(0, replay.Rows).Where(i => i % 5 == 0).ToArray();
        var x = new float[rows.Length * f];
        for (int k = 0; k < rows.Length; k++)
        {
            Array.Copy(replay.Features, rows[k] * f, x, k * f, f);
        }

        double Iou(DamageModelWeights model)
        {
            float[] p = MlpTrainer.Predict(model, x, rows.Length);
            double tp = 0, union = 0;
            for (int k = 0; k < rows.Length; k++)
            {
                bool pr = p[k] > model.Threshold, g = replay.Labels[rows[k]];
                double w = replay.Weights[rows[k]];
                if (pr && g) tp += w;
                if (pr || g) union += w;
            }

            return union == 0 ? 1.0 : tp / union;
        }

        return (Iou(current), Iou(candidate));
    }
}
