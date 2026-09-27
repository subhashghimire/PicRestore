using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using PicRestore.App.ViewModels;
using PicRestore.Core.Models;
using PicRestore.Imaging;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PicRestore.App.Views;

/// <summary>
/// The heart of the workflow: review/correct the proposed damage mask, run the restoration with clear,
/// staged progress feedback, then review and export the result - all on this one page, without forcing
/// a trip through Compare/Export just to see what happened. A damage brush adds damage the detector
/// missed; a protect brush marks a region (typically a face) that must never be touched, regardless of
/// what the detector or any later stage thinks. See the design plan's "Application Workflow & UX"
/// section and its "nothing is a black box" requirement.
/// </summary>
public sealed partial class MaskEditorPage : Page
{
    private enum ViewState { Editing, Processing, Result }

    private RestorationViewModel? _viewModel;
    private bool _isPointerDown;
    private bool _useProtectBrush;
    private List<TextBlock> _stageLabels = new();

    public MaskEditorPage()
    {
        InitializeComponent();
        _stageLabels = new List<TextBlock> { Stage1Text, Stage2Text, Stage3Text, Stage4Text, Stage5Text };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _viewModel = e.Parameter as RestorationViewModel;
        if (_viewModel is null)
        {
            return;
        }

        SetState(ViewState.Editing);

        SensitivitySlider.Value = _viewModel.Settings.DetectionSensitivity;
        BaseImage.Source = _viewModel.OriginalBitmap;
        _ = RefreshOverlayAsync();
        UpdateCoverageText();
    }

    /// <summary>
    /// The page has exactly three mutually exclusive states, and this is the only place that switches
    /// between them - every button handler below just calls this rather than toggling visibilities
    /// piecemeal, so the UI can never get stuck showing two states (e.g. the mask overlay and the
    /// result) at once.
    /// </summary>
    private void SetState(ViewState state)
    {
        // EditingControls is a StackPanel, and IsEnabled only exists on Control (not FrameworkElement/
        // Panel) in WinUI - it has to be set on each interactive control individually.
        bool editing = state == ViewState.Editing;
        SensitivitySlider.IsEnabled = editing;
        DamageBrushToggle.IsEnabled = editing;
        ProtectBrushToggle.IsEnabled = editing;
        BrushSizeSlider.IsEnabled = editing;
        RunButton.IsEnabled = editing;
        ProcessingOverlay.Visibility = state == ViewState.Processing ? Visibility.Visible : Visibility.Collapsed;
        ResultControls.Visibility = state == ViewState.Result ? Visibility.Visible : Visibility.Collapsed;

        MaskOverlayImage.Visibility = state == ViewState.Editing ? Visibility.Visible : Visibility.Collapsed;
        AfterImage.Visibility = state == ViewState.Result ? Visibility.Visible : Visibility.Collapsed;

        if (state != ViewState.Result)
        {
            HeatMapImage.Visibility = Visibility.Collapsed;
            if (HeatMapToggle.IsChecked == true)
            {
                HeatMapToggle.IsChecked = false;
            }
        }

        ExportOptions.Visibility = Visibility.Collapsed;

        // Don't let the user switch to Import/Compare/Export mid-run and land on a page that's showing
        // stale data for a restoration that's still in flight.
        (App.MainAppWindow as MainWindow)?.SetNavigationEnabled(state != ViewState.Processing);
    }

    private void SensitivitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.Settings.DetectionSensitivity = (float)e.NewValue;
        _viewModel.RedetectDamage();
        _ = RefreshOverlayAsync();
        UpdateCoverageText();
    }

    private void BrushToggle_Click(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, DamageBrushToggle))
        {
            ProtectBrushToggle.IsChecked = false;
            DamageBrushToggle.IsChecked = true;
            _useProtectBrush = false;
        }
        else
        {
            DamageBrushToggle.IsChecked = false;
            ProtectBrushToggle.IsChecked = true;
            _useProtectBrush = true;
        }
    }

    private void Overlay_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isPointerDown = true;
        PaintAt(e);
    }

    private void Overlay_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isPointerDown)
        {
            PaintAt(e);
        }
    }

    private void Overlay_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isPointerDown = false;
        _ = RefreshOverlayAsync();
        UpdateCoverageText();
    }

    private void PaintAt(PointerRoutedEventArgs e)
    {
        if (_viewModel?.Mask is not DamageMask mask)
        {
            return;
        }

        Windows.Foundation.Point point = e.GetCurrentPoint(MaskOverlayImage).Position;
        double renderedWidth = MaskOverlayImage.ActualWidth;
        double renderedHeight = MaskOverlayImage.ActualHeight;
        if (renderedWidth <= 0 || renderedHeight <= 0)
        {
            return;
        }

        int imageX = (int)(point.X / renderedWidth * mask.Width);
        int imageY = (int)(point.Y / renderedHeight * mask.Height);
        int radius = (int)BrushSizeSlider.Value;

        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if ((dx * dx) + (dy * dy) > radius * radius)
                {
                    continue;
                }

                int x = imageX + dx;
                int y = imageY + dy;
                if (x < 0 || y < 0 || x >= mask.Width || y >= mask.Height)
                {
                    continue;
                }

                if (_useProtectBrush)
                {
                    mask.Protect(x, y);
                }
                else
                {
                    mask.MarkDamaged(x, y);
                }
            }
        }
    }

    private async Task RefreshOverlayAsync()
    {
        if (_viewModel?.Mask is not DamageMask mask)
        {
            return;
        }

        MaskOverlayImage.Source = await MaskVisualizer.RenderAsync(mask, _viewModel.Settings.RepairThreshold);
    }

    private void UpdateCoverageText()
    {
        if (_viewModel?.Mask is not DamageMask mask)
        {
            return;
        }

        CoverageText.Text = $"{mask.CoveragePercentage(_viewModel.Settings.RepairThreshold):F1}% flagged for repair." +
            (mask.LockedFaces.Count > 0
                ? $" {mask.LockedFaces.Count} face(s) locked: only confident damage on faces is repaired (paint with the damage brush to repair more)."
                : "");
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        SetState(ViewState.Processing);
        ResetStageLabels();
        ProcessingBar.Value = 0;
        ProcessingStageText.Text = "Starting...";

        // Created here, on the UI thread, so Progress<T> captures this thread's SynchronizationContext -
        // every OnPipelineProgress callback below arrives back on the UI thread automatically, even
        // though the pipeline itself runs on a background thread (see RestorationViewModel.RunRestorationAsync).
        var progress = new Progress<PipelineProgress>(OnPipelineProgress);

        try
        {
            await _viewModel.RunRestorationAsync(progress);
            ShowResult();
        }
        catch (Exception ex)
        {
            SetState(ViewState.Editing);
            UpdateCoverageText();
            ShowStatus(InfoBarSeverity.Error, "Restoration failed", ex.Message);
        }
    }

    private void OnPipelineProgress(PipelineProgress progress)
    {
        ProcessingStageText.Text = $"Step {progress.StageIndex} of {progress.StageCount}: {progress.StageName}";
        ProcessingBar.Value = Math.Clamp((progress.StageIndex - 1) * 100.0 / progress.StageCount, 0, 100);

        for (int i = 0; i < _stageLabels.Count; i++)
        {
            int stageNumber = i + 1;
            TextBlock label = _stageLabels[i];
            if (stageNumber < progress.StageIndex)
            {
                label.Opacity = 1;
                label.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            }
            else if (stageNumber == progress.StageIndex)
            {
                label.Opacity = 1;
                label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            }
            else
            {
                label.Opacity = 0.5;
                label.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            }
        }
    }

    private void ResetStageLabels()
    {
        foreach (TextBlock label in _stageLabels)
        {
            label.Opacity = 0.5;
            label.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
        }
    }

    private void ShowResult()
    {
        if (_viewModel is null)
        {
            return;
        }

        ProcessingBar.Value = 100;
        foreach (TextBlock label in _stageLabels)
        {
            label.Opacity = 1;
            label.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
        }

        BaseImage.Source = _viewModel.OriginalBitmap;
        AfterImage.Source = _viewModel.ResultBitmap;
        CompareSlider.Value = 1;
        AfterImage.Opacity = CompareSlider.Value;

        string coverageSummary = _viewModel.Mask is DamageMask mask
            ? $"{mask.CoveragePercentage(_viewModel.Settings.RepairThreshold):F1}% of the photo was flagged and repaired. "
            : string.Empty;
        ResultSummaryText.Text = $"{coverageSummary}Drag the slider to compare, or export when you're happy with it.";

        SetState(ViewState.Result);
        ShowStatus(InfoBarSeverity.Success, "Restoration complete", "Compare the result below, then export.");
    }

    private void CompareSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        AfterImage.Opacity = e.NewValue;
    }

    private async void HeatMapToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.Mask is null)
        {
            return;
        }

        if (HeatMapToggle.IsChecked == true)
        {
            HeatMapImage.Source = await MaskVisualizer.RenderAsync(_viewModel.Mask, _viewModel.Settings.RepairThreshold);
            HeatMapImage.Visibility = Visibility.Visible;
        }
        else
        {
            HeatMapImage.Visibility = Visibility.Collapsed;
        }
    }

    private void BackToEditingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        BaseImage.Source = _viewModel.OriginalBitmap;
        _ = RefreshOverlayAsync();
        UpdateCoverageText();
        SetState(ViewState.Editing);
    }

    private void OpenCompareButton_Click(object sender, RoutedEventArgs e)
    {
        (App.MainAppWindow as MainWindow)?.NavigateToCompare();
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        ExportOptions.Visibility = ExportOptions.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private async void ConfirmExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        OutputFormat format = FormatButtons.SelectedIndex == 0 ? OutputFormat.Png : OutputFormat.Jpeg;

        var picker = new FileSavePicker();
        System.IntPtr hwnd = WindowNative.GetWindowHandle(App.MainAppWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        picker.SuggestedFileName = "restored-photo";
        if (format == OutputFormat.Png)
        {
            picker.FileTypeChoices.Add("PNG image", new List<string> { ".png" });
        }
        else
        {
            picker.FileTypeChoices.Add("JPEG image", new List<string> { ".jpg" });
        }

        Windows.Storage.StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            _viewModel.SaveResult(file.Path, format, (int)QualitySlider.Value);
            ExportOptions.Visibility = Visibility.Collapsed;
            ShowStatus(InfoBarSeverity.Success, "Exported", $"Saved to {file.Path}");
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Export failed", ex.Message);
        }
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusInfoBar.Severity = severity;
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.IsOpen = true;
    }
}
