using System.Windows;
using System.Windows.Controls;
using Muted.App.ViewModels;

namespace Muted.App.Views;

public partial class DashboardView : System.Windows.Controls.UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    // Press and hold is a mouse gesture, not a command, so it lives in code-behind.
    private void BypassButton_OnPressed(object sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is MainViewModel viewModel && BypassButton.IsEnabled)
        {
            viewModel.BeginBypass();
        }
    }

    private void BypassButton_OnReleased(object sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.EndBypass();
        }
    }
}
