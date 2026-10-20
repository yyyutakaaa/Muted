using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Muted.App.ViewModels;

namespace Muted.App.Views;

public partial class AdvancedPanel : System.Windows.Controls.UserControl
{
    public AdvancedPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// A profile picked here is applied straight away. Selection that arrives through
    /// the binding (loading settings, another page) is left alone.
    /// </summary>
    private void ProfilePicker_OnSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (DataContext is not MainViewModel viewModel ||
            eventArgs.AddedItems.Count == 0 ||
            !(ProfilePicker.IsDropDownOpen || ProfilePicker.IsKeyboardFocusWithin || Mouse.Captured is not null))
        {
            return;
        }

        // Let the binding hand SelectedProfile to the view model before applying it.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (viewModel.ApplyProfileCommand.CanExecute(null))
            {
                viewModel.ApplyProfileCommand.Execute(null);
            }
        });
    }

    private void ProfileMenuButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ProfileMenuButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = ProfileMenuButton;
            menu.Placement = PlacementMode.Bottom;
            menu.VerticalOffset = 6;
            menu.IsOpen = true;
        }
    }

    private void ManageProfiles_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SelectedPage = AppPage.Settings;
        }
    }
}
