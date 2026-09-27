using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using PicRestore.App.ViewModels;
using PicRestore.Core.Imaging;
using PicRestore.Imaging;
using PicRestore.Restoration.Training;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PicRestore.App.Views;

/// <summary>
/// Restoration options plus the damage-model training workflow: add before/after pairs, train (manually
/// or automatically after each new pair), review each pair's damage map, and reset to the built-in model.
/// </summary>
public sealed partial class SettingsPage : Page
{
    private RestorationViewModel? _viewModel;
    private List<TrainingPairInfo> _pairs = new();
    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
    }

    private ModelTrainingService? Training => _viewModel?.Training;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _viewModel = e.Parameter as RestorationViewModel;
        if (_viewModel is null)
        {
            return;
        }

        _loading = true;
        SharpeningSlider.Value = System.Math.Round(_viewModel.Settings.SharpeningAmount * 100);
        StrictGuardToggle.IsOn = _viewModel.Settings.StrictIdentityGuard;
        LockFacesToggle.IsOn = _viewModel.Settings.LockFaces;
        FaceLockText.Text = _viewModel.FaceDetectionUnavailableReason is { } reason
            ? $"Face detection is unavailable ({reason}), so faces can't be locked automatically - use the protect brush."
            : "Faces are found automatically (YuNet); intact facial features are never repainted.";
        if (Training is not null)
        {
            AutoTrainToggle.IsOn = Training.RetrainAutomatically;
        }

        _loading = false;
        RefreshTrainingSection();
    }

    private void SharpeningSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_viewModel is null || _loading)
        {
            return;
        }

        _viewModel.Settings.SharpeningAmount = (float)(e.NewValue / 100.0);
    }

    private void StrictGuardToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || _loading)
        {
            return;
        }

        _viewModel.Settings.StrictIdentityGuard = StrictGuardToggle.IsOn;
    }

    private void LockFacesToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || _loading)
        {
            return;
        }

        _viewModel.Settings.LockFaces = LockFacesToggle.IsOn;
        _viewModel.RedetectDamage();
    }

    private void AutoTrainToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (Training is null || _loading)
        {
            return;
        }

        Training.RetrainAutomatically = AutoTrainToggle.IsOn;
    }

    private void RefreshTrainingSection()
    {
        if (Training is null)
        {
            ActiveModelText.Text = "Training is unavailable: " + (_viewModel?.TrainingUnavailableReason ?? "unknown reason");
            AddPairButton.IsEnabled = TrainButton.IsEnabled = ResetModelButton.IsEnabled = AutoTrainToggle.IsEnabled = false;
            return;
        }

        ActiveModelText.Text = Training.DescribeActiveModel();
        ResetModelButton.IsEnabled = !Training.IsUsingBuiltInModel && !Training.IsTraining;
        _pairs = Training.Pairs.ToList();
        PairsList.ItemsSource = _pairs
            .Select(p => $"{p.Name}  -  {p.DamageShare:P1} marked as damage  -  added {p.AddedUtc.LocalDateTime:yyyy-MM-dd HH:mm}")
            .ToList();
        NoPairsText.Visibility = _pairs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TrainButton.IsEnabled = _pairs.Count > 0 && !Training.IsTraining;
        RemovePairButton.IsEnabled = PairsList.SelectedIndex >= 0 && !Training.IsTraining;
        HistoryText.Text = string.Join(System.Environment.NewLine, Training.History.Take(12));
        LibraryFolderText.Text = $"Stored in {Training.LibraryFolder}";
    }

    private async void AddPairButton_Click(object sender, RoutedEventArgs e)
    {
        if (Training is null)
        {
            return;
        }

        string? damaged = await PickImageAsync("Use as damaged original");
        if (damaged is null)
        {
            return;
        }

        ShowStatus(InfoBarSeverity.Informational, "Now pick the restored version", $"Original: {System.IO.Path.GetFileName(damaged)}");
        string? restored = await PickImageAsync("Use as restored version");
        if (restored is null)
        {
            TrainingInfoBar.IsOpen = false;
            return;
        }

        SetBusy(true);
        try
        {
            var status = new System.Progress<string>(message => TrainingStatusText.Text = message);
            TrainingPairInfo info = await Training.AddPairAsync(damaged, restored, status);
            RefreshTrainingSection();
            PairsList.SelectedIndex = _pairs.FindIndex(p => p.Id == info.Id);
            ShowStatus(InfoBarSeverity.Success, "Pair added",
                $"{info.DamageShare:P1} of the photo was marked as damage. Check the red areas in the preview below - " +
                "if they cover things that aren't damage, remove the pair.");
            TrainingStatusText.Text = string.Empty;

            if (Training.RetrainAutomatically)
            {
                await RunTrainingAsync();
            }
        }
        catch (System.Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Couldn't add this pair", ex.Message);
            TrainingStatusText.Text = string.Empty;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void TrainButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            await RunTrainingAsync();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async System.Threading.Tasks.Task RunTrainingAsync()
    {
        if (Training is null)
        {
            return;
        }

        TrainingProgressBar.Visibility = Visibility.Visible;
        TrainingProgressBar.Value = 0;
        var progress = new System.Progress<TrainingProgress>(p =>
        {
            TrainingProgressBar.Value = p.Fraction * 100;
            TrainingStatusText.Text = p.Stage;
        });

        try
        {
            TrainingReport report = await Training.TrainAsync(progress);
            string perPair = string.Join("; ", report.Pairs.Select(p => $"{p.Name}: {p.CurrentIoU:P0} -> {p.CandidateIoU:P0}"));
            ShowStatus(report.Accepted ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                report.Accepted ? "New model in use" : "Kept the current model",
                $"{report.Summary} Per pair: {perPair}.");
        }
        catch (System.Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Training failed", ex.Message);
        }
        finally
        {
            TrainingProgressBar.Visibility = Visibility.Collapsed;
            TrainingStatusText.Text = string.Empty;
            RefreshTrainingSection();
        }
    }

    private async void ResetModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (Training is null)
        {
            return;
        }

        await Training.ResetToBuiltInAsync();
        ShowStatus(InfoBarSeverity.Informational, "Back to the built-in model", "Your training pairs are kept; train again whenever you like.");
        RefreshTrainingSection();
    }

    private async void PairsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int index = PairsList.SelectedIndex;
        RemovePairButton.IsEnabled = index >= 0 && Training is not null && !Training.IsTraining;
        if (Training is null || index < 0 || index >= _pairs.Count)
        {
            PairPreviewImage.Source = null;
            PairPreviewCaption.Text = string.Empty;
            return;
        }

        TrainingPairInfo info = _pairs[index];
        string path = Training.PreviewPath(info.Id);
        PairPreviewCaption.Text =
            $"{info.DamagedFileName} + {info.RestoredFileName}: red = learned as damage, dimmed = not covered by the restored image.";
        try
        {
            RasterImage preview = await System.Threading.Tasks.Task.Run(() => ImageIO.Load(path));
            PairPreviewImage.Source = await BitmapConverter.ToWriteableBitmapAsync(preview);
        }
        catch (System.Exception)
        {
            PairPreviewImage.Source = null;
        }
    }

    private void RemovePairButton_Click(object sender, RoutedEventArgs e)
    {
        int index = PairsList.SelectedIndex;
        if (Training is null || index < 0 || index >= _pairs.Count)
        {
            return;
        }

        Training.RemovePair(_pairs[index].Id);
        PairPreviewImage.Source = null;
        PairPreviewCaption.Text = string.Empty;
        RefreshTrainingSection();
    }

    private void SetBusy(bool busy)
    {
        AddPairButton.IsEnabled = !busy && Training is not null;
        TrainButton.IsEnabled = !busy && _pairs.Count > 0;
        ResetModelButton.IsEnabled = !busy && Training is not null && !Training.IsUsingBuiltInModel;
        RemovePairButton.IsEnabled = !busy && PairsList.SelectedIndex >= 0;
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        TrainingInfoBar.Severity = severity;
        TrainingInfoBar.Title = title;
        TrainingInfoBar.Message = message;
        TrainingInfoBar.IsOpen = true;
    }

    private static async System.Threading.Tasks.Task<string?> PickImageAsync(string commitText)
    {
        var picker = new FileOpenPicker { CommitButtonText = commitText };
        System.IntPtr hwnd = WindowNative.GetWindowHandle(App.MainAppWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
        foreach (string extension in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" })
        {
            picker.FileTypeFilter.Add(extension);
        }

        Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }
}
