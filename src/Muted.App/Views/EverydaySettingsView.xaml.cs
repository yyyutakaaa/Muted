using System.Windows;
using Muted.App.ViewModels;
namespace Muted.App.Views;
public partial class EverydaySettingsView : System.Windows.Controls.UserControl
{
    public EverydaySettingsView() => InitializeComponent();
    private void Done_OnClick(object sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) vm.SelectedPage = AppPage.Dashboard; }
    private void Diagnostics_OnClick(object sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) vm.SelectedPage = AppPage.Diagnostics; }
}
