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
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainAppWindow = new MainWindow();
        MainAppWindow.Activate();
    }

    /// <summary>
    /// Last-resort safety net. Without this, any exception that escapes a page's event handler (or a
    /// background Task an event handler forgot to catch) brings the whole process down silently - no
    /// dialog, no log, nothing. This at least records what happened to a file the developer can read,
    /// and stops the crash so the window stays open.
    /// </summary>
    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        try
        {
            string logDirectory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PicRestore");
            System.IO.Directory.CreateDirectory(logDirectory);

            string logPath = System.IO.Path.Combine(logDirectory, "crash.log");
            System.IO.File.AppendAllText(
                logPath,
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{e.Exception}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}");
        }
        catch
        {
            // Logging is best-effort only - never let the crash handler itself throw.
        }
    }
}
