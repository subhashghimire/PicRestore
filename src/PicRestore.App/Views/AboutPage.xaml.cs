using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PicRestore.App.ViewModels;

namespace PicRestore.App.Views;

/// <summary>The restoration protocol PicRestore follows, and how each part of it is implemented.</summary>
public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        string version = typeof(AboutPage).Assembly.GetName().Version?.ToString(3) ?? "dev";
        string model = (e.Parameter as RestorationViewModel)?.Training?.DescribeActiveModel() ?? "Built-in damage detection model.";
        VersionText.Text = $"Version {version}. {model}";
    }
}
