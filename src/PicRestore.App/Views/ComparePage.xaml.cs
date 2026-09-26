using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using PicRestore.App.ViewModels;

namespace PicRestore.App.Views;

/// <summary>
/// Before/after comparison via an opacity cross-fade slider, plus an optional overlay of the damage
/// mask as a confidence heat map and a plain-text run history - see the design plan's "Application
/// Workflow & UX" section.
/// </summary>
public sealed partial class ComparePage : Page
{
    private RestorationViewModel? _viewModel;
    private bool _heatMapVisible;

    public ComparePage()
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

        BeforeImage.Source = _viewModel.OriginalBitmap;
        AfterImage.Source = _viewModel.ResultBitmap;
        AfterImage.Opacity = CompareSlider.Value;
        HistoryList.ItemsSource = _viewModel.History;
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

        _heatMapVisible = !_heatMapVisible;
        if (_heatMapVisible)
        {
            HeatMapImage.Source = await MaskVisualizer.RenderAsync(_viewModel.Mask, _viewModel.Settings.RepairThreshold);
            HeatMapImage.Visibility = Visibility.Visible;
        }
        else
        {
            HeatMapImage.Visibility = Visibility.Collapsed;
        }
    }
}
