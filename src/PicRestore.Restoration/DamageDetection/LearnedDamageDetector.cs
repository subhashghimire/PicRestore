using PicRestore.Core.Abstractions;
using PicRestore.Core.Imaging;
using PicRestore.Core.Models;
using PicRestore.Restoration.Imaging;

namespace PicRestore.Restoration.DamageDetection;

/// <summary>
/// Learned damage detector: a small multilayer perceptron (34 -> 32 -> 16 -> 1) over generic
/// per-pixel colour and local-statistics features (<see cref="DamageFeatures"/>), run at a fixed working
/// resolution and upsampled back to the photo.
///
/// <para>
/// Trained on five before/after pairs (a heavily silvered Polaroid and four lightly damaged family
/// prints; see tools/damage-model). Held-out blocks of those photos: damage-map overlap (IoU) 0.52 on the
/// heavily damaged print and 0.02-0.19 on the lightly damaged ones, whose damage is mostly tiny specks.
/// Honest limitation: on a photo it has never seen (leave-one-photo-out) the overlap is only 0.02-0.09 -
/// five pairs is too little for this to generalise, and it can mistake busy texture (foliage, soil) for
/// damage. That is why the defaults are conservative (sensitivity 0.2, face lock, locked intact pixels),
/// why the Mask Editor is always the review step, and why it can keep learning from your own pairs
/// (Settings -> Damage detection model, see Training.DamageModelTrainer).
/// </para>
///
/// Sensitivity shifts the model's decision boundary in logit space, so higher sensitivity always flags
/// a superset of what lower sensitivity flags. The raw model output is cached per image, so moving the
/// sensitivity slider only re-calibrates instead of re-running the network.
/// </summary>
public sealed class LearnedDamageDetector : IDamageDetector
{
    /// <summary>Long side of the working image the model runs at (the scale it was trained at).</summary>
    public const int WorkingLongSide = 1280;

    /// <summary>Widest hidden layer the per-thread buffers support.</summary>
    public const int MaxLayerWidth = 64;

    private readonly object _cacheLock = new();
    private DamageModelWeights _model;

    public LearnedDamageDetector(DamageModelWeights? model = null)
    {
        _model = model ?? DamageModelWeights.BuiltIn;
        _model.Validate();
    }

    /// <summary>
    /// The network in use. Swapping it (e.g. after in-app training) takes effect on the next
    /// <see cref="Detect"/> call; the cached probabilities from the previous model are discarded.
    /// </summary>
    public DamageModelWeights Model
    {
        get => _model;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            value.Validate();
            lock (_cacheLock)
            {
                _model = value;
                _cachedImage = null;
                _cachedProbability = null;
            }
        }
    }

    /// <summary>
    /// The model's raw (uncalibrated) damage probability at working resolution, plus that resolution.
    /// Used by in-app training to score candidate models on the same footing as detection.
    /// </summary>
    public static float[] PredictWorking(DamageModelWeights model, float[] features, int width, int height)
    {
        float[] p = Predict(model, features, width * height);
        return ImageOps.BoxMean(p, width, height, model.SmoothRadius);
    }
    private RasterImage? _cachedImage;
    private float[]? _cachedProbability; // full-resolution raw model probability (pre-calibration)

    public DamageMask Detect(RasterImage image, ProcessingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(settings);

        float[] probability = RawProbability(image);

        // The model's natural decision threshold (Model.Threshold) lands exactly on the repair threshold at
        // sensitivity 0.4; each 0.1 of sensitivity moves the boundary by 0.4 in logit space. The default
        // sensitivity (0.2) is therefore deliberately stricter than the model's own threshold.
        float shift = Logit(settings.RepairThreshold) - Logit(_model.Threshold)
            + (4f * (Math.Clamp(settings.DetectionSensitivity, 0f, 1f) - 0.4f));

        var mask = new DamageMask(image.Width, image.Height);
        for (int i = 0; i < probability.Length; i++)
        {
            float p = Sigmoid(Logit(probability[i]) + shift);
            mask.Probability[i] = p;
            mask.Type[i] = p > 0.05f ? DamageType.MetallicOrFoxingSpeckle : DamageType.None;
        }

        return mask;
    }

    private float[] RawProbability(RasterImage image)
    {
        lock (_cacheLock)
        {
            if (ReferenceEquals(image, _cachedImage) && _cachedProbability is not null)
            {
                return _cachedProbability;
            }
        }

        int width = image.Width;
        int height = image.Height;
        double scale = Math.Min(1.0, (double)WorkingLongSide / Math.Max(width, height));
        int workWidth = Math.Max(1, (int)Math.Round(width * scale));
        int workHeight = Math.Max(1, (int)Math.Round(height * scale));

        var (r, g, b) = Planar(image);
        if (workWidth != width || workHeight != height)
        {
            r = ImageOps.ResizeArea(r, width, height, workWidth, workHeight);
            g = ImageOps.ResizeArea(g, width, height, workWidth, workHeight);
            b = ImageOps.ResizeArea(b, width, height, workWidth, workHeight);
        }

        DamageModelWeights model = _model;
        float[] features = DamageFeatures.Compute(r, g, b, workWidth, workHeight);
        float[] work = PredictWorking(model, features, workWidth, workHeight);

        float[] full = workWidth == width && workHeight == height
            ? work
            : ImageOps.ResizeBilinear(work, workWidth, workHeight, width, height);

        lock (_cacheLock)
        {
            if (ReferenceEquals(model, _model))
            {
                _cachedImage = image;
                _cachedProbability = full;
            }
        }

        return full;
    }

    private static float[] Predict(DamageModelWeights model, float[] features, int pixels)
    {
        const int f = DamageFeatures.Count;
        float[] mean = model.FeatureMean;
        float[] scale = model.FeatureScale;
        float[][] weights = model.Weights;
        float[][] biases = model.Biases;
        int[] sizes = model.LayerSizes; // input, hidden..., output

        var output = new float[pixels];
        Parallel.For(0, pixels, () => (new float[MaxLayerWidth], new float[MaxLayerWidth]), (i, _, buffers) =>
        {
            (float[] current, float[] next) = buffers;
            for (int k = 0; k < f; k++)
            {
                current[k] = (features[(i * f) + k] - mean[k]) / scale[k];
            }

            for (int layer = 0; layer < weights.Length; layer++)
            {
                int inSize = sizes[layer];
                int outSize = sizes[layer + 1];
                float[] w = weights[layer]; // row-major [in, out]
                float[] bias = biases[layer];
                for (int o = 0; o < outSize; o++)
                {
                    float sum = bias[o];
                    for (int k = 0; k < inSize; k++)
                    {
                        sum += current[k] * w[(k * outSize) + o];
                    }

                    next[o] = layer < weights.Length - 1 ? MathF.Max(sum, 0f) : sum;
                }

                (current, next) = (next, current);
            }

            output[i] = Sigmoid(current[0]);
            return (current, next);
        }, _ => { });

        return output;
    }

    private static (float[] R, float[] G, float[] B) Planar(RasterImage image)
    {
        int n = image.Width * image.Height;
        var r = new float[n];
        var g = new float[n];
        var b = new float[n];
        float[] px = image.Pixels;
        for (int i = 0; i < n; i++)
        {
            r[i] = px[i * 4];
            g[i] = px[(i * 4) + 1];
            b[i] = px[(i * 4) + 2];
        }

        return (r, g, b);
    }

    private static float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));

    private static float Logit(float p)
    {
        p = Math.Clamp(p, 1e-6f, 1f - 1e-6f);
        return MathF.Log(p / (1f - p));
    }
}
