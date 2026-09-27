using System.Text.Json;
using System.Text.Json.Serialization;

namespace PicRestore.Restoration.DamageDetection;

/// <summary>
/// The learned damage detector's parameters: feature standardisation, the MLP's layer weights and
/// biases, and its calibrated decision threshold. <see cref="BuiltIn"/> is the model that ships with the
/// app (generated into <c>LearnedDamageModel.g.cs</c>); models the user trains in-app from their own
/// before/after pairs are instances of this class saved as JSON.
/// </summary>
public sealed class DamageModelWeights
{
    public string Name { get; init; } = "Built-in";

    /// <summary>When this model was produced (UTC).</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>How many before/after photo pairs contributed to this model (0 = unknown / built-in).</summary>
    public int TrainingPairCount { get; init; }

    public required int[] LayerSizes { get; init; }

    public required float[] FeatureMean { get; init; }

    public required float[] FeatureScale { get; init; }

    /// <summary>Per layer, row-major [inputs, outputs].</summary>
    public required float[][] Weights { get; init; }

    public required float[][] Biases { get; init; }

    public float Threshold { get; init; } = 0.4f;

    public int SmoothRadius { get; init; } = 3;

    /// <summary>Validation IoU recorded when this model was accepted (null for the built-in model).</summary>
    public double? ValidationIoU { get; init; }

    private static readonly Lazy<DamageModelWeights> BuiltInModel = new(() => new DamageModelWeights
    {
        Name = "Built-in",
        TrainingPairCount = LearnedDamageModel.TrainingPairCount,
        LayerSizes = (int[])LearnedDamageModel.LayerSizes.Clone(),
        FeatureMean = (float[])LearnedDamageModel.FeatureMean.Clone(),
        FeatureScale = (float[])LearnedDamageModel.FeatureScale.Clone(),
        Weights = LearnedDamageModel.Weights.Select(w => (float[])w.Clone()).ToArray(),
        Biases = LearnedDamageModel.Biases.Select(b => (float[])b.Clone()).ToArray(),
        Threshold = LearnedDamageModel.Threshold,
        SmoothRadius = LearnedDamageModel.SmoothRadius,
    });

    /// <summary>The model that ships with the app.</summary>
    public static DamageModelWeights BuiltIn => BuiltInModel.Value;

    /// <summary>A deep copy with a new name/metadata, e.g. as the starting point for further training.</summary>
    public DamageModelWeights With(string? name = null, int? pairCount = null, double? validationIoU = null, float[][]? weights = null, float[][]? biases = null) => new()
    {
        Name = name ?? Name,
        CreatedUtc = DateTimeOffset.UtcNow,
        TrainingPairCount = pairCount ?? TrainingPairCount,
        LayerSizes = (int[])LayerSizes.Clone(),
        FeatureMean = (float[])FeatureMean.Clone(),
        FeatureScale = (float[])FeatureScale.Clone(),
        Weights = (weights ?? Weights).Select(w => (float[])w.Clone()).ToArray(),
        Biases = (biases ?? Biases).Select(b => (float[])b.Clone()).ToArray(),
        Threshold = Threshold,
        SmoothRadius = SmoothRadius,
        ValidationIoU = validationIoU ?? ValidationIoU,
    };

    /// <summary>Throws if the arrays don't describe a consistent network for <see cref="DamageFeatures"/>.</summary>
    public void Validate()
    {
        if (LayerSizes.Length < 2 || LayerSizes[0] != DamageFeatures.Count || LayerSizes[^1] != 1)
        {
            throw new InvalidDataException("Damage model has an unexpected layer layout.");
        }

        if (FeatureMean.Length != DamageFeatures.Count || FeatureScale.Length != DamageFeatures.Count
            || Weights.Length != LayerSizes.Length - 1 || Biases.Length != LayerSizes.Length - 1)
        {
            throw new InvalidDataException("Damage model arrays don't match its layer layout.");
        }

        for (int l = 0; l < Weights.Length; l++)
        {
            if (Weights[l].Length != LayerSizes[l] * LayerSizes[l + 1] || Biases[l].Length != LayerSizes[l + 1])
            {
                throw new InvalidDataException($"Damage model layer {l} has the wrong size.");
            }
        }

        if (LayerSizes.Skip(1).Max() > LearnedDamageDetector.MaxLayerWidth)
        {
            throw new InvalidDataException("Damage model layers are wider than the detector supports.");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static DamageModelWeights FromJson(string json)
    {
        DamageModelWeights model = JsonSerializer.Deserialize<DamageModelWeights>(json, JsonOptions)
            ?? throw new InvalidDataException("Empty damage model file.");
        model.Validate();
        return model;
    }
}
