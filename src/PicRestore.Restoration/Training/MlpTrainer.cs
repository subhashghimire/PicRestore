using PicRestore.Restoration.DamageDetection;

namespace PicRestore.Restoration.Training;

/// <summary>
/// Trains the damage detector's small MLP (ReLU hidden layers, sigmoid output, weighted binary
/// cross-entropy) with Adam, starting from an existing model's weights so each round of training builds
/// on what the model already knows. Feature standardisation is kept fixed from the starting model.
/// </summary>
public static class MlpTrainer
{
    public sealed record Options
    {
        public int MaxEpochs { get; init; } = 12;
        public int BatchSize { get; init; } = 256;
        public double LearningRate { get; init; } = 5e-4;
        public double L2 { get; init; } = 1e-4;
        public int Patience { get; init; } = 3;
        public int Seed { get; init; } = 0;
    }

    /// <summary>Trains on row-major raw features (rows x <see cref="DamageFeatures.Count"/>).</summary>
    /// <param name="onEpoch">Called after each epoch with (epoch, maxEpochs, validation loss).</param>
    public static DamageModelWeights Train(
        DamageModelWeights start,
        float[] features,
        bool[] labels,
        float[] weights,
        Options? options = null,
        Action<int, int, double>? onEpoch = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new Options();
        const int f = DamageFeatures.Count;
        int rows = labels.Length;
        if (features.Length != rows * f || weights.Length != rows)
        {
            throw new ArgumentException("Feature, label and weight counts don't match.");
        }

        // Standardise once with the model's fixed scaler.
        var x = new float[features.Length];
        for (int i = 0; i < rows; i++)
        {
            for (int k = 0; k < f; k++)
            {
                x[(i * f) + k] = (features[(i * f) + k] - start.FeatureMean[k]) / start.FeatureScale[k];
            }
        }

        int[] sizes = start.LayerSizes;
        int layers = sizes.Length - 1;
        float[][] w = start.Weights.Select(a => (float[])a.Clone()).ToArray();
        float[][] b = start.Biases.Select(a => (float[])a.Clone()).ToArray();
        float[][] mW = w.Select(a => new float[a.Length]).ToArray(), vW = w.Select(a => new float[a.Length]).ToArray();
        float[][] mB = b.Select(a => new float[a.Length]).ToArray(), vB = b.Select(a => new float[a.Length]).ToArray();

        var rng = new Random(options.Seed);
        int[] order = Enumerable.Range(0, rows).OrderBy(_ => rng.Next()).ToArray();
        int validationCount = Math.Max(1, rows / 10);
        int[] validation = order[..validationCount];
        int[] training = order[validationCount..];

        float[][] bestW = w.Select(a => (float[])a.Clone()).ToArray(), bestB = b.Select(a => (float[])a.Clone()).ToArray();
        double bestLoss = Loss(x, labels, weights, validation, w, b, sizes);
        int stale = 0;
        long step = 0;
        int threads = Math.Max(1, Math.Min(Environment.ProcessorCount, 8));

        for (int epoch = 1; epoch <= options.MaxEpochs; epoch++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Shuffle(training, rng);
            for (int start0 = 0; start0 < training.Length; start0 += options.BatchSize)
            {
                int end = Math.Min(start0 + options.BatchSize, training.Length);
                var gradW = new float[threads][][];
                var gradB = new float[threads][][];
                var weightSum = new double[threads];
                Parallel.For(0, threads, t =>
                {
                    gradW[t] = w.Select(a => new float[a.Length]).ToArray();
                    gradB[t] = b.Select(a => new float[a.Length]).ToArray();
                    var act = sizes.Select(s => new float[s]).ToArray();
                    var delta = sizes.Select(s => new float[s]).ToArray();
                    for (int j = start0 + t; j < end; j += threads)
                    {
                        int i = training[j];
                        float sw = weights[i];
                        weightSum[t] += sw;
                        Forward(x, i, w, b, sizes, act);
                        // Output delta for sigmoid + cross-entropy: (p - y) * weight.
                        delta[layers][0] = (act[layers][0] - (labels[i] ? 1f : 0f)) * sw;
                        for (int l = layers - 1; l >= 0; l--)
                        {
                            int inSize = sizes[l], outSize = sizes[l + 1];
                            float[] gw = gradW[t][l], wl = w[l];
                            for (int o = 0; o < outSize; o++)
                            {
                                float d = delta[l + 1][o];
                                if (d == 0f)
                                {
                                    continue;
                                }

                                gradB[t][l][o] += d;
                                for (int k = 0; k < inSize; k++)
                                {
                                    gw[(k * outSize) + o] += act[l][k] * d;
                                }
                            }

                            if (l > 0)
                            {
                                for (int k = 0; k < inSize; k++)
                                {
                                    float sum = 0;
                                    for (int o = 0; o < outSize; o++)
                                    {
                                        sum += wl[(k * outSize) + o] * delta[l + 1][o];
                                    }

                                    delta[l][k] = act[l][k] > 0f ? sum : 0f; // ReLU derivative
                                }
                            }
                        }
                    }
                });

                double totalWeight = Math.Max(weightSum.Sum(), 1e-9);
                step++;
                const double beta1 = 0.9, beta2 = 0.999, epsilon = 1e-8;
                double lr = options.LearningRate * Math.Sqrt(1 - Math.Pow(beta2, step)) / (1 - Math.Pow(beta1, step));
                for (int l = 0; l < layers; l++)
                {
                    for (int k = 0; k < w[l].Length; k++)
                    {
                        double g = 0;
                        for (int t = 0; t < threads; t++)
                        {
                            g += gradW[t][l][k];
                        }

                        g = (g / totalWeight) + (options.L2 * w[l][k]);
                        mW[l][k] = (float)((beta1 * mW[l][k]) + ((1 - beta1) * g));
                        vW[l][k] = (float)((beta2 * vW[l][k]) + ((1 - beta2) * g * g));
                        w[l][k] -= (float)(lr * mW[l][k] / (Math.Sqrt(vW[l][k]) + epsilon));
                    }

                    for (int k = 0; k < b[l].Length; k++)
                    {
                        double g = 0;
                        for (int t = 0; t < threads; t++)
                        {
                            g += gradB[t][l][k];
                        }

                        g /= totalWeight;
                        mB[l][k] = (float)((beta1 * mB[l][k]) + ((1 - beta1) * g));
                        vB[l][k] = (float)((beta2 * vB[l][k]) + ((1 - beta2) * g * g));
                        b[l][k] -= (float)(lr * mB[l][k] / (Math.Sqrt(vB[l][k]) + epsilon));
                    }
                }
            }

            double loss = Loss(x, labels, weights, validation, w, b, sizes);
            onEpoch?.Invoke(epoch, options.MaxEpochs, loss);
            if (loss < bestLoss - 1e-5)
            {
                bestLoss = loss;
                bestW = w.Select(a => (float[])a.Clone()).ToArray();
                bestB = b.Select(a => (float[])a.Clone()).ToArray();
                stale = 0;
            }
            else if (++stale >= options.Patience)
            {
                break;
            }
        }

        return start.With(weights: bestW, biases: bestB);
    }

    /// <summary>Raw (unsmoothed) probabilities for standardised-on-the-fly feature rows.</summary>
    public static float[] Predict(DamageModelWeights model, float[] features, int rows)
    {
        const int f = DamageFeatures.Count;
        var x = new float[features.Length];
        for (int i = 0; i < rows; i++)
        {
            for (int k = 0; k < f; k++)
            {
                x[(i * f) + k] = (features[(i * f) + k] - model.FeatureMean[k]) / model.FeatureScale[k];
            }
        }

        var p = new float[rows];
        Parallel.For(0, rows, () => model.LayerSizes.Select(s => new float[s]).ToArray(), (i, _, act) =>
        {
            Forward(x, i, model.Weights, model.Biases, model.LayerSizes, act);
            p[i] = act[^1][0];
            return act;
        }, _ => { });
        return p;
    }

    private static void Forward(float[] x, int row, float[][] w, float[][] b, int[] sizes, float[][] act)
    {
        int f = sizes[0];
        Array.Copy(x, row * f, act[0], 0, f);
        int layers = sizes.Length - 1;
        for (int l = 0; l < layers; l++)
        {
            int inSize = sizes[l], outSize = sizes[l + 1];
            float[] wl = w[l], input = act[l], output = act[l + 1];
            for (int o = 0; o < outSize; o++)
            {
                float sum = b[l][o];
                for (int k = 0; k < inSize; k++)
                {
                    sum += input[k] * wl[(k * outSize) + o];
                }

                output[o] = l < layers - 1 ? MathF.Max(sum, 0f) : 1f / (1f + MathF.Exp(-sum));
            }
        }
    }

    private static double Loss(float[] x, bool[] labels, float[] weights, int[] rows, float[][] w, float[][] b, int[] sizes)
    {
        double total = 0, weightSum = 0;
        object gate = new();
        Parallel.ForEach(System.Collections.Concurrent.Partitioner.Create(0, rows.Length), () => (0.0, 0.0), (range, _, acc) =>
        {
            var act = sizes.Select(s => new float[s]).ToArray();
            for (int j = range.Item1; j < range.Item2; j++)
            {
                int i = rows[j];
                Forward(x, i, w, b, sizes, act);
                double p = Math.Clamp(act[^1][0], 1e-7, 1 - 1e-7);
                acc.Item1 += weights[i] * (labels[i] ? -Math.Log(p) : -Math.Log(1 - p));
                acc.Item2 += weights[i];
            }

            return acc;
        }, acc =>
        {
            lock (gate)
            {
                total += acc.Item1;
                weightSum += acc.Item2;
            }
        });

        return total / Math.Max(weightSum, 1e-9);
    }

    private static void Shuffle(int[] a, Random rng)
    {
        for (int i = a.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (a[i], a[j]) = (a[j], a[i]);
        }
    }
}
