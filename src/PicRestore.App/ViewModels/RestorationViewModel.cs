using System.Collections.ObjectModel;
using PicRestore.Core.Abstractions;
using Microsoft.UI.Xaml.Media.Imaging;
using PicRestore.Core.Imaging;
using PicRestore.Core.Models;
using PicRestore.Imaging;
using PicRestore.Ml;
using PicRestore.Restoration;
using PicRestore.Restoration.Color;
using PicRestore.Restoration.DamageDetection;
using PicRestore.Restoration.Enhancement;
using PicRestore.Restoration.Identity;
using PicRestore.Restoration.Inpainting;
using PicRestore.Restoration.Texture;
using PicRestore.Restoration.Training;

namespace PicRestore.App.ViewModels;

/// <summary>
/// Shared state for the whole app: the loaded photo, its damage mask, the user's settings, and the
/// pipeline that turns them into a restored image. One instance is created in <see cref="MainWindow"/>
/// and passed to every page, so Import, Mask Editor, Compare and Export all see the same project - see
/// the design plan's "Application Workflow & UX" section.
/// </summary>
public sealed class RestorationViewModel : ObservableObject
{
    // Learned detector; its model can be improved in-app from before/after pairs (Settings page) - see
    // ModelTrainingService. The rule-based CompositeDamageDetector is kept for tests and comparison.
    private readonly LearnedDamageDetector _detector = new();

    // LaMa AI inpainting; its model is downloaded/loaded lazily on the first restoration (see
    // PrepareAiModelAsync). Until then - or if that fails - it falls back to the classical diffusion fill
    // and says so in the history.
    private readonly LamaInpainter _inpainter = new();
    private readonly RestorationPipeline _pipeline;
    private OnnxLamaModel? _lamaModel;

    // YuNet face detector for the protocol's face lock (null if its model couldn't be loaded).
    private readonly IFaceDetector? _faceDetector;
    private IReadOnlyList<FaceRegion> _faces = Array.Empty<FaceRegion>();

    private RasterImage? _originalImage;
    private RasterImage? _resultImage;
    private WriteableBitmap? _originalBitmap;
    private WriteableBitmap? _resultBitmap;
    private DamageMask? _mask;
    private string _statusMessage = "Import a photo to begin.";

    public RestorationViewModel()
    {
        try
        {
            _faceDetector = OnnxFaceDetector.Create();
        }
        catch (Exception ex)
        {
            FaceDetectionUnavailableReason = ex.Message;
        }

        try
        {
            Training = new ModelTrainingService(new TrainingLibrary(TrainingLibrary.DefaultRoot), ApplyDamageModelAsync);
            _detector.Model = Training.ActiveModel;
        }
        catch (Exception ex)
        {
            // Training storage unavailable (e.g. no write access): detection still works with the built-in model.
            TrainingUnavailableReason = ex.Message;
        }

        // NullFaceEnhancer is the safe default: it does nothing. Swap in an OnnxFaceEnhancer (or another
        // IFaceEnhancer) once a GFPGAN/CodeFormer/GPEN model is actually wired up - see the design plan's
        // Technology Stack section. The pipeline itself already gates the call on Settings.EnableAiEnhance.
        _pipeline = new RestorationPipeline(
            new HistogramColorRestorer(),
            _inpainter,
            new GrainMatcher(),
            new SimpleFaceIdentityGuard(),
            new NullFaceEnhancer());
    }

    public ProcessingSettings Settings { get; } = ProcessingSettings.CreateDefault();

    /// <summary>In-app detector training (null if its storage folder couldn't be created).</summary>
    public ModelTrainingService? Training { get; }

    public string? TrainingUnavailableReason { get; }

    public string? FaceDetectionUnavailableReason { get; }

    /// <summary>Faces found in the current photo (locked against repair unless damage there is confident).</summary>
    public IReadOnlyList<FaceRegion> Faces => _faces;

    /// <summary>Damage detection followed by the face lock (restoration protocol, "locked elements").</summary>
    private DamageMask DetectWithLocks(RasterImage image)
    {
        DamageMask mask = _detector.Detect(image, Settings);
        FaceLock.Apply(mask, _faces, Settings);
        return mask;
    }

    private string DescribeMask(DamageMask mask) =>
        $"{mask.CoveragePercentage(Settings.RepairThreshold):F1}% flagged for repair" +
        (mask.LockedFaces.Count > 0 ? $"; {mask.LockedFaces.Count} face(s) locked" : "") + ".";

    public RestorationProject? Project { get; private set; }

    public ObservableCollection<PipelineStageResult> History { get; } = new();

    public DamageMask? Mask => _mask;

    public WriteableBitmap? OriginalBitmap
    {
        get => _originalBitmap;
        private set => SetField(ref _originalBitmap, value);
    }

    public WriteableBitmap? ResultBitmap
    {
        get => _resultBitmap;
        private set => SetField(ref _resultBitmap, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool HasPhoto => _originalImage is not null;

    public async Task LoadPhotoAsync(string filePath)
    {
        RasterImage loaded = await Task.Run(() => ImageIO.Load(filePath));
        _originalImage = loaded;
        Project = new RestorationProject { OriginalFilePath = filePath, Settings = Settings };

        // Face and damage detection run small neural networks over the whole photo (a few seconds on a
        // large scan) - keep them off the UI thread. Later sensitivity changes reuse the cached output.
        _faces = _faceDetector is null
            ? Array.Empty<FaceRegion>()
            : await Task.Run(() => _faceDetector.Detect(loaded));
        _mask = await Task.Run(() => DetectWithLocks(loaded));
        Project.Mask = _mask;

        OriginalBitmap = await BitmapConverter.ToWriteableBitmapAsync(_originalImage);
        _resultImage = _originalImage;
        ResultBitmap = OriginalBitmap;

        StatusMessage =
            $"Loaded {System.IO.Path.GetFileName(filePath)} - {DescribeMask(_mask)} Review the mask before restoring.";
    }

    /// <summary>Re-runs detection - e.g. after the user moves the sensitivity slider in the Mask Editor.</summary>
    public void RedetectDamage()
    {
        if (_originalImage is null)
        {
            return;
        }

        _mask = DetectWithLocks(_originalImage);
        if (Project is not null)
        {
            Project.Mask = _mask;
        }

        StatusMessage = DescribeMask(_mask);
    }

    /// <param name="progress">
    /// Reports each pipeline stage as it starts. Construct the <see cref="Progress{T}"/> on the UI
    /// thread (e.g. in the page's click handler) - it captures that thread's context, so callbacks
    /// arrive back on the UI thread automatically even though the pipeline itself runs on a background
    /// thread via <see cref="Task.Run{TResult}(Func{TResult})"/> below.
    /// </param>
    public async Task RunRestorationAsync(IProgress<PipelineProgress>? progress = null)
    {
        if (_originalImage is null || _mask is null)
        {
            StatusMessage = "Import a photo first.";
            return;
        }

        StatusMessage = "Restoring... this can take a minute or two for large or heavily damaged photos.";
        History.Clear();

        await PrepareAiModelAsync(progress);

        // The classical pipeline (diffusion inpainting especially) is CPU-bound and can run for a
        // meaningful amount of time on a large, heavily-damaged photo. Running it directly on the
        // caller's thread would freeze the UI for that whole duration, since this method is invoked
        // from a button click handler on the UI thread - Task.Run moves the actual work off it.
        RasterImage originalImage = _originalImage;
        DamageMask mask = _mask;
        ProcessingSettings settings = Settings;
        RestorationProject? project = Project;

        _resultImage = await Task.Run(() => _pipeline.Run(originalImage, mask, settings, project, progress));
        ResultBitmap = await BitmapConverter.ToWriteableBitmapAsync(_resultImage);

        if (Project is not null)
        {
            foreach (PipelineStageResult entry in Project.History)
            {
                History.Add(entry);
            }
        }

        StatusMessage = "Restoration complete. Compare the result, then export.";
    }

    /// <summary>
    /// Makes sure the LaMa model is on disk (downloading it once, ~90 MB) and loaded. Any failure is
    /// non-fatal: the inpainter falls back to the classical fill and the reason appears in the history.
    /// </summary>
    private async Task PrepareAiModelAsync(IProgress<PipelineProgress>? progress)
    {
        if (_lamaModel is not null)
        {
            return;
        }

        try
        {
            var download = new Progress<double>(fraction => progress?.Report(new PipelineProgress(
                $"Preparing: downloading the AI repair model (one time only, ~90 MB) - {fraction:P0}",
                1,
                RestorationPipeline.StageCount)));
            string path = await LamaModelStore.EnsureAsync(download);

            progress?.Report(new PipelineProgress("Preparing: loading the AI repair model", 1, RestorationPipeline.StageCount));
            _lamaModel = await Task.Run(() => new OnnxLamaModel(path));
            _inpainter.Model = _lamaModel;
            _inpainter.ModelUnavailableReason = null;
        }
        catch (Exception ex)
        {
            _inpainter.Model = null;
            _inpainter.ModelUnavailableReason = ex.Message;
        }
    }

    /// <summary>
    /// Switches detection to <paramref name="model"/> (after training or a reset) and, if a photo is
    /// loaded, re-detects its damage so the Mask Editor reflects the new model.
    /// </summary>
    private async Task ApplyDamageModelAsync(DamageModelWeights model)
    {
        _detector.Model = model;
        if (_originalImage is null)
        {
            return;
        }

        RasterImage image = _originalImage;
        _mask = await Task.Run(() => DetectWithLocks(image));
        if (Project is not null)
        {
            Project.Mask = _mask;
        }

        StatusMessage = $"Damage map updated with the new detection model: {DescribeMask(_mask)}";
    }

    public void SaveResult(string destinationPath, OutputFormat format, int jpegQuality = 95)
    {
        if (_resultImage is null)
        {
            throw new InvalidOperationException("Nothing to export yet - run a restoration first.");
        }

        ImageIO.Save(_resultImage, destinationPath, format, jpegQuality);
        StatusMessage = $"Exported to {destinationPath}.";
    }
}
