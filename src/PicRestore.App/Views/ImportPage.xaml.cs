using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PicRestore.App.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PicRestore.App.Views;

public sealed partial class ImportPage : Page
{
    private RestorationViewModel? _viewModel;

    public ImportPage()
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

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        StatusText.Text = _viewModel.StatusMessage;
        PreviewImage.Source = _viewModel.OriginalBitmap;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (args.PropertyName == nameof(RestorationViewModel.OriginalBitmap))
        {
            PreviewImage.Source = _viewModel.OriginalBitmap;
        }
        else if (args.PropertyName == nameof(RestorationViewModel.StatusMessage))
        {
            StatusText.Text = _viewModel.StatusMessage;
        }
    }

    private async void ImportButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        var picker = new FileOpenPicker();
        System.IntPtr hwnd = WindowNative.GetWindowHandle(App.MainAppWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".tiff");

        Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await _viewModel.LoadPhotoAsync(file.Path);
        }
    }
}
