using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PicRestore.App.ViewModels;
using PicRestore.Imaging;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PicRestore.App.Views;

public sealed partial class ExportPage : Page
{
    private RestorationViewModel? _viewModel;

    public ExportPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _viewModel = e.Parameter as RestorationViewModel;
    }

    private async void ExportButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
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
            StatusText.Text = $"Saved to {file.Path}";
        }
        catch (System.Exception ex)
        {
            StatusText.Text = $"Export failed: {ex.Message}";
        }
    }
}
