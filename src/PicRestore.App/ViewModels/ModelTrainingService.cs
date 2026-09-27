using PicRestore.Core.Imaging;
using PicRestore.Imaging;
using PicRestore.Restoration.DamageDetection;
using PicRestore.Restoration.Training;

namespace PicRestore.App.ViewModels;

/// <summary>
/// The app side of continuous damage-detector training: turns a (damaged, restored) file pair into a
/// stored training pair, runs training rounds in the background, and hands every accepted model to the
/// detector. All heavy work runs off the UI thread; every step is logged to the training history.
/// </summary>
public sealed class ModelTrainingService
{
    private readonly TrainingLibrary _library;
    private readonly Func<DamageModelWeights, Task> _applyModel;
    private readonly Lazy<ReplaySet?> _replay = new(ReplaySet.LoadBuiltIn);
    private readonly SemaphoreSlim _trainingGate = new(1, 1);

    public ModelTrainingService(TrainingLibrary library, Func<DamageModelWeights, Task> applyModel)
    {
        _library = library;
        _applyModel = applyModel;
        ActiveModel = library.LoadActiveModel() ?? DamageModelWeights.BuiltIn;
    }

    /// <summary>The detector model in use (the built-in one until a trained model has been accepted).</summary>
    public DamageModelWeights ActiveModel { get; private set; }

    public bool IsUsingBuiltInModel => ReferenceEquals(ActiveModel, DamageModelWeights.BuiltIn);

    public bool IsTraining { get; private set; }

    public string LibraryFolder => _library.Root;

    public IReadOnlyList<TrainingPairInfo> Pairs => _library.ListPairs();

    public IReadOnlyList<string> History => _library.ReadHistory();

    public bool RetrainAutomatically
    {
        get => _library.LoadSettings().RetrainAutomatically;
        set => _library.SaveSettings(_library.LoadSettings() with { RetrainAutomatically = value });
    }

    public string PreviewPath(string pairId) => Path.Combine(_library.PairDirectory(pairId), "preview.png");

    public string DescribeActiveModel()
    {
        if (IsUsingBuiltInModel)
        {
            return $"Built-in model (trained on {ActiveModel.TrainingPairCount} before/after pairs).";
        }

        string score = ActiveModel.ValidationIoU is double iou ? $", held-out overlap {iou:P0}" : "";
        return $"{ActiveModel.Name} - your trained model ({ActiveModel.TrainingPairCount} pairs in total{score}).";
    }

    /// <summary>Aligns the two images, builds the damage map, stores the pair and its review preview.</summary>
    public Task<TrainingPairInfo> AddPairAsync(string damagedPath, string restoredPath, IProgress<string>? status, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            status?.Report("Loading both images...");
            RasterImage damaged = ImageIO.Load(damagedPath);
            RasterImage restored = ImageIO.Load(restoredPath);

            status?.Report("Lining up the restored version with the original...");
            PairAlignment alignment = PairAligner.Align(damaged, restored, cancellationToken);
            if (alignment.Score < PairAligner.MinimumScore)
            {
                throw new InvalidOperationException(
                    $"These two images don't seem to show the same photo (match score {alignment.Score:F2}). " +
                    "Pick the damaged scan first, then its restored version.");
            }

            status?.Report("Mapping where the restoration differs from the original...");
            string name = Path.GetFileNameWithoutExtension(damagedPath);
            TrainingPair pair = GroundTruthBuilder.Build(name, damaged, restored, alignment);
            TrainingPairInfo info = _library.AddPair(pair, Path.GetFileName(damagedPath), Path.GetFileName(restoredPath), alignment.Score);
            ImageIO.Save(GroundTruthBuilder.Preview(pair), PreviewPath(info.Id), OutputFormat.Png);
            _library.AppendHistory($"Added pair \"{info.Name}\": {info.DamageShare:P1} of the photo marked as damage (match score {alignment.Score:F2}).");
            return info;
        }, cancellationToken);
    }

    public void RemovePair(string pairId)
    {
        TrainingPairInfo? info = Pairs.FirstOrDefault(p => p.Id == pairId);
        _library.RemovePair(pairId);
        if (info is not null)
        {
            _library.AppendHistory($"Removed pair \"{info.Name}\" (the current model keeps what it learned until you retrain or reset).");
        }
    }

    /// <summary>
    /// Runs one training round on every stored pair. A new model is only adopted if it scores at least as
    /// well as the current one on held-out regions and keeps the built-in knowledge (see DamageModelTrainer).
    /// </summary>
    public async Task<TrainingReport> TrainAsync(IProgress<TrainingProgress>? progress, CancellationToken cancellationToken = default)
    {
        await _trainingGate.WaitAsync(cancellationToken);
        IsTraining = true;
        try
        {
            IReadOnlyList<TrainingPair> pairs = await Task.Run(() => _library.LoadAllPairs(), cancellationToken);
            if (pairs.Count == 0)
            {
                throw new InvalidOperationException("Add at least one before/after pair first.");
            }

            DamageModelWeights current = ActiveModel;
            TrainingReport report = await Task.Run(
                () => DamageModelTrainer.Train(pairs, current, _replay.Value, progress, null, cancellationToken),
                cancellationToken);

            if (report.Accepted)
            {
                _library.SaveActiveModel(report.Model);
                ActiveModel = report.Model;
                await _applyModel(report.Model);
            }

            _library.AppendHistory($"{report.Summary} ({report.Duration.TotalSeconds:F0} s)");
            return report;
        }
        finally
        {
            IsTraining = false;
            _trainingGate.Release();
        }
    }

    public async Task ResetToBuiltInAsync()
    {
        _library.ResetModel();
        ActiveModel = DamageModelWeights.BuiltIn;
        await _applyModel(ActiveModel);
    }
}
