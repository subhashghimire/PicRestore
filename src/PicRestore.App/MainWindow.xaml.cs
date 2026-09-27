using Microsoft.UI.Xaml.Controls;
using PicRestore.App.ViewModels;
using PicRestore.App.Views;

namespace PicRestore.App;

/// <summary>
/// PicRestore's single window. A NavigationView switches between the four workflow pages (Import,
/// Mask Editor, Compare, Export) plus Settings (restoration options and damage-model training) and
/// About (the restoration protocol); all of them share one <see cref="RestorationViewModel"/> instance
/// so the loaded photo, mask, and settings stay in sync across the whole workflow.
/// </summary>
public sealed partial class MainWindow : Microsoft.UI.Xaml.Window
{
    public RestorationViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        Title = "PicRestore";
        try
        {
            // Title-bar and taskbar icon (the .exe itself gets it from ApplicationIcon in the csproj).
            AppWindow.SetIcon(System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "PicRestore.ico"));
        }
        catch (System.Exception)
        {
            // Cosmetic only - never block startup over a missing icon.
        }

        ContentFrame.Navigate(typeof(ImportPage), ViewModel);
    }

    /// <summary>
    /// Lets the current page temporarily lock the navigation pane - used while a restoration is
    /// running, so the user can't switch pages mid-run and land in a confusing half-finished state.
    /// </summary>
    public void SetNavigationEnabled(bool enabled) => RootNavigationView.IsEnabled = enabled;

    /// <summary>
    /// Sends the user to the full Compare page (history, heat map) while keeping the nav pane's
    /// selection highlight in sync, since this is triggered from a link inside Mask Editor rather than
    /// from the pane itself.
    /// </summary>
    public void NavigateToCompare() => CompareItem.IsSelected = true;

    private void RootNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage), ViewModel);
            return;
        }

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
            "About" => typeof(AboutPage),
            _ => typeof(ImportPage)
        };

        ContentFrame.Navigate(pageType, ViewModel);
    }
}
