using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Muted.App.Services;

namespace Muted.App.Views;

public partial class AboutView : System.Windows.Controls.UserControl
{
    private const string SupportUrl = "https://www.buymeacoffee.com/yyyutakaaa";

    public AboutView()
    {
        InitializeComponent();
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        VersionText.Text = version is null ? "Version unknown" : $"Version {version.ToString(3)}";
    }

    private void SupportButton_OnClick(object sender, RoutedEventArgs eventArgs) => Shell.Open(SupportUrl);
}
