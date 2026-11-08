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
        VersionText.Text = $"Version {Infrastructure.AppVersion.Display} · {Infrastructure.AppVersion.ChannelAndBuild}";
    }

    private void SupportButton_OnClick(object sender, RoutedEventArgs eventArgs) => Shell.Open(SupportUrl);
}
