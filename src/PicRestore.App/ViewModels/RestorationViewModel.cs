using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Media.Imaging;
using PicRestore.Core.Imaging;
using PicRestore.Core.Models;
using PicRestore.Imaging;
using PicRestore.Restoration;
using PicRestore.Restoration.Color;
using PicRestore.Restoration.DamageDetection;
using PicRestore.Restoration.Enhancement;
using PicRestore.Restoration.Identity;
using PicRestore.Restoration.Inpainting;
using PicRestore.Restoration.Texture;

namespace PicRestore.App.ViewModels;

/// <summary>
/// Shared state for the whole app: the loaded photo, its damage mask, the user's settings, and the
/// pipeline that turns them into a restored image. One instance is created in <see cref="MainWindow"/>
/// and passed to every page, so Import, Mask Editor, Compare and Export all see the same project - see
/// the design plan's "Application Workflow & UX" section.
/// </summary>
public sealed class RestorationViewModel : ObservableObject
{
    private readonly CompositeDamageDetector _detector = new();

    // NullFaceEnhancer is the safe default: it does nothing. Swap in an OnnxFaceEnhancer (or another
    // IFaceEnhancer) once a GFPGAN/CodeFormer/GPEN model is actually wired up - see the design plan's
    // Technology Stack section. The pipeline itself already gates the call on Settings.EnableAiEnhance.
    private readonly RestorationPipeline _pipeline = new(
        new HistogramColorRestorer(),
        new DiffusionInpainter(),
        new GrainMatcher(),
        new SimpleFaceIdentityGuard(),
        new NullFaceEnhancer());

    private RasterImage? _originalImage;
    private RasterImage? _resultImage;
    private WriteableBitmap? _originalBitmap;
    private WriteableBitmap? _resultBitmap;
    private DamageMask? _mask;
    private string _statusMessage = "Import a photo to begin.";

    public ProcessingSettings Settings { get; } = ProcessingSettings.CreateDefault();

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
        _originalImage = ImageIO.Load(filePath);
        Project = new RestorationProject { OriginalFilePath = filePath, Settings = Settings };

        _mask = _detector.Detect(_originalImage, Settings);
        Project.Mask = _mask;

        OriginalBitmap = await BitmapConverter.ToWriteableBitmapAsync(_originalImage);
        _resultImage = _originalImage;
        ResultBitmap = OriginalBitmap;

        StatusMessage =
            $"Loaded {System.IO.Path.GetFileName(filePath)} - " +
            $"{_mask.CoveragePercentage(Settings.RepairThreshold):F1}% flagged for repair. Review the mask before restoring.";
    }

    /// <summary>Re-runs detection - e.g. after the user moves the sensitivity slider in the Mask Editor.</summary>
    public void RedetectDamage()
    {
        if (_originalImage is null)
        {
            return;
        }

        _mask = _detector.Detect(_originalImage, Settings);
        if (Project is not null)
        {
            Project.Mask = _mask;
        }

        StatusMessage = $"{_mask.CoveragePercentage(Settings.RepairThreshold):F1}% of the photo is flagged for repair.";
    }

    public async Task RunRestorationAsync()
    {
        if (_originalImage is null || _mask is null)
        {
            StatusMessage = "Import a photo first.";
            return;
        }

        History.Clear();
        _resultImage = _pipeline.Run(_originalImage, _mask, Settings, Project);
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
