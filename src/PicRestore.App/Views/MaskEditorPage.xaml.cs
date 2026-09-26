using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using PicRestore.App.ViewModels;
using PicRestore.Core.Models;

namespace PicRestore.App.Views;

/// <summary>
/// Lets the user review and correct the automatically proposed damage mask before anything is
/// repaired - see the design plan's "Damage Detection & Masking" section. A damage brush adds damage
/// the detector missed; a protect brush marks a region (typically a face) that must never be touched,
/// regardless of what the detector or any later stage thinks.
/// </summary>
public sealed partial class MaskEditorPage : Page
{
    private RestorationViewModel? _viewModel;
    private bool _isPointerDown;
    private bool _useProtectBrush;

    public MaskEditorPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _viewModel = e.Parameter as RestorationViewModel;
        if (_viewModel is null)
        {
            return;
        }

        SensitivitySlider.Value = _viewModel.Settings.DetectionSensitivity;
        BaseImage.Source = _viewModel.OriginalBitmap;
        _ = RefreshOverlayAsync();
        UpdateCoverageText();
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

        CoverageText.Text = $"{mask.CoveragePercentage(_viewModel.Settings.RepairThreshold):F1}% flagged for repair.";
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        await _viewModel.RunRestorationAsync();
    }
}
