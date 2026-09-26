using Microsoft.UI.Xaml.Controls;
using PicRestore.App.ViewModels;
using PicRestore.App.Views;

namespace PicRestore.App;

/// <summary>
/// PicRestore's single window. A NavigationView switches between the four workflow pages (Import,
/// Mask Editor, Compare, Export); all of them share one <see cref="RestorationViewModel"/> instance so
/// the loaded photo, mask, and settings stay in sync across the whole workflow.
/// </summary>
public sealed partial class MainWindow : Microsoft.UI.Xaml.Window
{
    public RestorationViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        Title = "PicRestore";
        ContentFrame.Navigate(typeof(ImportPage), ViewModel);
    }

    private void RootNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is not NavigationViewItem item || item.Tag is not string tag)
        {
            return;
        }

        System.Type pageType = tag switch
        {
            "Import" => typeof(ImportPage),
            "MaskEditor" => typeof(MaskEditorPage),
            "Compare" => typeof(ComparePage),
            "Export" => typeof(ExportPage),
            _ => typeof(ImportPage)
        };

        ContentFrame.Navigate(pageType, ViewModel);
    }
}
