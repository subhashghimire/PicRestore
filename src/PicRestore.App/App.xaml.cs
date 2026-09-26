using Microsoft.UI.Xaml;

namespace PicRestore.App;

public partial class App : Application
{
    /// <summary>
    /// The single main window, exposed so pages can get its handle for WinRT interop (file pickers).
    /// PicRestore is a single-window app - see the design plan's Application Workflow & UX section.
    /// </summary>
    public static Window? MainAppWindow { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainAppWindow = new MainWindow();
        MainAppWindow.Activate();
    }
}
